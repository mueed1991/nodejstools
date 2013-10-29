﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NpmTests
{
    /// <summary>
    /// A class to manange a set of temporary files. The temporary files are deleted when Dispose or the finalizer is called.
    /// </summary>
    /// <remarks>
    /// Each TemporaryFileManager can have a pre- and postfix applied to each file/directory.
    /// </remarks>
    public class TemporaryFileManager
    {
        #region Static variables

        public const string DEFAULT_EXTENSION = "tmp";
        private static string s_TempPath;
        private static readonly object s_StaticLock = new object();

        #endregion

        #region Static Properties

        /// <summary>
        /// Returns the default temporary directory used for all temporary files.
        /// </summary>
        public string DefaultTempPath
        {
            get
            {
                lock (s_StaticLock)
                {
                    if (s_TempPath == null)
                    {
                        InitializeTempPath();
                    }
                    return s_TempPath;
                }
            }
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Generates a new file or directory name with the default extension.
        /// This will be unmanaged by any <see cref="TemporaryFileManager"/> instance.
        /// </summary>
        /// <returns>A new temporary file or directory name.</returns>
        public string GenerateName()
        {
            return GenerateName("", DEFAULT_EXTENSION);
        }

        /// <summary>
        /// Generates a new file or directory name with the specified extension.
        /// This will be unmanaged by any <see cref="TemporaryFileManager"/> instance.
        /// </summary>
        /// <param name="extension">The extension to apply to the name, without the dot. Can be null or empty for no extension.</param>
        /// <returns>A new temporary file or directory name.</returns>
        public string GenerateName(string extension)
        {
            return GenerateName("", extension);
        }

        /// <summary>
        /// Generates a new file or directory name with the specified prefix and extension.
        /// This will be unmanaged by any <see cref="TemporaryFileManager"/> instance.
        /// </summary>
        /// <param name="prefix">The prefix to apply to the name.</param>
        /// <param name="extension">The extension to apply to the name, without the dot. Can be null or empty for no extension.</param>
        /// <returns>A new temporary file or directory name.</returns>
        public string GenerateName(string prefix, string extension)
        {
            StringBuilder name = new StringBuilder(String.Format("{0}{1}", prefix, Guid.NewGuid()));
            // add the extension if specified
            if (!String.IsNullOrEmpty(extension))
            {
                name.Append('.').Append(extension);
            }
            return name.ToString();
        }

        private static void InitializeTempPath()
        {
            // first try to use RGTEMP or a subdirectory of TempPath
            string temppath = Environment.GetEnvironmentVariable("RGTEMP");
            if (String.IsNullOrEmpty(temppath))
            {
                temppath = Path.Combine(Path.GetTempPath(), @"Red Gate\");
            }

            string filename = null;
            FileStream fs = null;
            try
            {
                // create it if it doesnt exist
                if (!Directory.Exists(temppath))
                {
                    Directory.CreateDirectory(temppath);
                }

                // and check we can actually create a file
                filename = Path.Combine(temppath, Guid.NewGuid().ToString());
                fs = File.Create(filename);
            }
            catch
            {
                temppath = Path.GetTempPath();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                    File.Delete(filename);
                }
            }
            s_TempPath = temppath;
        }

        #endregion

        #region Member variables

        // the full paths to all the known files & directories managed by this instance
        // the boolean indicates if it should be deleted on disposal
        private readonly IDictionary<string, bool> m_Files = new Dictionary<string, bool>();
        private readonly IDictionary<string, bool> m_Directories = new Dictionary<string, bool>();
        private readonly string m_Prefix;
        private readonly string m_Extension;
        private readonly object m_Lock = new object();

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporaryFileManager"/> class with an empty prefix.
        /// </summary>
        public TemporaryFileManager()
            : this("", DEFAULT_EXTENSION)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporaryFileManager"/> class with the specified prefix.
        /// </summary>
        /// <param name="prefix">The prefix to apply to all temporary file and directory names.</param>
        public TemporaryFileManager(string prefix)
            : this(prefix, DEFAULT_EXTENSION)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporaryFileManager"/> class with the specified prefix
        /// and default extension.
        /// </summary>
        /// <param name="prefix">The prefix to apply to all temporary file and directory names.</param>
        /// <param name="defaultExtension">The default extension for all new temporary files (can be overridden per-file).</param>
        public TemporaryFileManager(string prefix, string defaultExtension)
        {
            m_Prefix = prefix;
            m_Extension = defaultExtension;
        }

        ~TemporaryFileManager()
        {
            Dispose(false);
        }

        #endregion

        #region IDisposable implementation and ancillary methods

        private bool m_Disposed;
        private string m_ObjectName;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns a value indicating if this instance has been disposed.
        /// </summary>
        public bool IsDisposed
        {
            get { return m_Disposed; }
        }

        /// <summary>
        /// Gets and sets the object name to use for any thrown <see cref="ObjectDisposedException"/>s. The default
        /// is the Name property of <see cref="Object.GetType()"/>.
        /// </summary>
        protected string ObjectName
        {
            get { return !String.IsNullOrEmpty(m_ObjectName) ? m_ObjectName : GetType().Name; }
            set { m_ObjectName = value; }
        }

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> if this instance has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        protected void CheckDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(ObjectName);
            }
        }

        #endregion

        #region Generating directory & filenames

        /// <summary>
        /// Generates a unique filename within the specified subdirectory of <see cref="DefaultTempPath"/>.
        /// </summary>
        /// <param name="parentDir">The subdirectory under which to create the file, relative to the <see cref="DefaultTempPath"/>.</param>
        /// <param name="extension">The extension to apply to the filename, without the dot.</param>
        /// <returns>The full path to the temporary file.</returns>
        public string GenerateUniqueFilePath(string parentDir, string extension)
        {
            CheckDisposed();

            string filePath;
            lock (m_Lock)
            {
                do
                {
                    do
                    {
                        filePath = Path.Combine(
                            Path.Combine(DefaultTempPath, parentDir),
                            GenerateName(m_Prefix, extension));
                    } while (m_Files.ContainsKey(filePath));
                } while (File.Exists(filePath) || Directory.Exists(filePath));
            }
            return filePath;
        }

        /// <summary>
        /// Generates a unique directory name within the specified subdirectory of <see cref="DefaultTempPath"/>
        /// </summary>
        /// <returns>The full path to the directory.</returns>
        public string GenerateUniqueDirectoryPath()
        {
            CheckDisposed();

            string fullPath;
            lock (m_Lock)
            {
                do
                {
                    do
                    {
                        fullPath = Path.Combine(DefaultTempPath, GenerateName(m_Prefix, ""));
                    } while (m_Directories.ContainsKey(fullPath));
                } while (File.Exists(fullPath) || Directory.Exists(fullPath));
            }

            return fullPath;
        }

        #endregion

        #region Creating files

        //[Obsolete("Please use GetNewTempFile instead")]
        //public string GetTempFilename() {
        //    return GetNewTempFile();
        //}

        //[Obsolete("Please use GetNewTempFile instead")]
        //public string GetTempFilename(bool createFile) {
        //    return GetNewTempFile(createFile, true);
        //}

        /// <summary>
        /// Creates a new temporary file in <see cref="DefaultTempPath"/> that will be deleted on disposal of the current instance.
        /// </summary>
        /// <returns>The full path of the temporary file.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have permissions to create a file on <see cref="DefaultTempPath"/>.</exception>
        /// <exception cref="IOException">An I/O error occurred while creating the file.</exception>
        public string GetNewTempFile()
        {
            return GetNewTempFile("", m_Extension, true, true);
        }

        /// <summary>
        /// Creates a new temporary file with the specified extension in <see cref="DefaultTempPath"/>
        /// that will be deleted on disposal of the current instance.
        /// </summary>
        /// <param name="extension">The extension to apply to the file.</param>
        /// <returns>The full path of the temporary file.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have permissions to create a file on <see cref="DefaultTempPath"/>.</exception>
        /// <exception cref="IOException">An I/O error occurred while creating the file.</exception>
        public string GetNewTempFile(string extension)
        {
            return GetNewTempFile("", extension, true, true);
        }

        /// <summary>
        /// Creates a new temporary file with the specified extension in the specified subdirectory of <see cref="DefaultTempPath"/>
        /// that will be deleted on disposal of the current instance.
        /// </summary>
        /// <param name="subDir">The subdirectory of <see cref="DefaultTempPath"/> to create the file in.</param>
        /// <param name="extension">The extension to apply to the file.</param>
        /// <returns>The full path of the temporary file.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have permissions to create a file on <see cref="DefaultTempPath"/>.</exception>
        /// <exception cref="DirectoryNotFoundException">The specified subdirectory <paramref name="subDir"/> does not exist.</exception>
        /// <exception cref="IOException">An I/O error occurred while creating the file.</exception>
        public string GetNewTempFile(string subDir, string extension)
        {
            return GetNewTempFile(subDir, extension, true, true);
        }

        /// <summary>
        /// Returns the path to a new temporary file in <see cref="DefaultTempPath"/>.
        /// </summary>
        /// <param name="createFile">If <em>true</em>, the file is also created.</param>
        /// <param name="deleteOnDispose">If <em>true</em>, the file will be deleted on disposal of this instance if it exists.</param>
        /// <returns>The full path of the temporary file.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have permissions to create a file on <see cref="DefaultTempPath"/>.</exception>
        /// <exception cref="IOException">An I/O error occurred while creating the file.</exception>
        public string GetNewTempFile(bool createFile, bool deleteOnDispose)
        {
            return GetNewTempFile("", m_Extension, createFile, deleteOnDispose);
        }

        /// <summary>
        /// Returns the path to a new temporary file with the specified extension
        /// in the specified subdirectory of <see cref="DefaultTempPath"/>.
        /// </summary>
        /// <param name="subDir">The subdirectory to create the temporary file under.</param>
        /// <param name="extension">The extension to apply to the file.</param>
        /// <param name="createFile">If <em>true</em>, the file is also created.</param>
        /// <param name="deleteOnDispose">If <em>true</em>, the file will be deleted on disposal of this instance if it exists.</param>
        /// <returns>The path of the temporary file.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have permissions to create a file on <see cref="DefaultTempPath"/>.</exception>
        /// <exception cref="DirectoryNotFoundException">The specified subdirectory <paramref name="subDir"/> does not exist.</exception>
        /// <exception cref="IOException">An I/O error occurred while creating the file.</exception>
        public virtual string GetNewTempFile(string subDir, string extension, bool createFile, bool deleteOnDispose)
        {
            CheckDisposed();

            lock (m_Lock)
            {
                string filename = GenerateUniqueFilePath(subDir, extension);
                if (createFile)
                {
                    FileStream stream = null;
                    try
                    {
                        using (stream = File.Create(filename))
                        {
                            m_Files.Add(filename, deleteOnDispose);
                        }
                    }
                    finally
                    {
                        if (stream != null)
                        {
                            stream.Close();
                        }
                    }
                }
                else
                {
                    m_Files.Add(filename, deleteOnDispose);
                }

                return filename;
            }
        }

        #endregion

        #region Creating directories

        //[Obsolete("Please use GetNewTempDirectory instead")]
        //public string GetTempSubdirectoryName() {
        //    return GetNewTempDirectory().FullName;
        //}

        //[Obsolete("Please use GetNewTempDirectory instead", true)]
        //public string GetTempSubdirectoryName(bool createDirectory) {
        //    // we don't optionally create directories anymore
        //    throw new NotSupportedException();
        //}

        /// <summary>
        /// Creates a new temporary directory in <see cref="DefaultTempPath"/> that will be deleted on disposal of the current instance.
        /// </summary>
        /// <returns>A <see cref="DirectoryInfo"/> object representing the new directory.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have the necessary permissions to create a temporary directory.</exception>
        public DirectoryInfo GetNewTempDirectory()
        {
            return GetNewTempDirectory(true);
        }

        /// <summary>
        /// Creates a new temporary directory in <see cref="DefaultTempPath"/>.
        /// </summary>
        /// <param name="deleteOnDispose">
        /// If <em>true</em>, the directory and its contents will be recursively deleted on disposal of this instance, if it exists.
        /// </param>
        /// <returns>A <see cref="DirectoryInfo"/> object representing the new directory.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have the necessary permissions to create a temporary directory.</exception>
        public virtual DirectoryInfo GetNewTempDirectory(bool deleteOnDispose)
        {
            CheckDisposed();

            lock (m_Lock)
            {
                string dirpath = GenerateUniqueDirectoryPath();
                DirectoryInfo dirinfo = Directory.CreateDirectory(dirpath);

                m_Directories.Add(dirpath, deleteOnDispose);
                return dirinfo;
            }
        }

        #endregion

        #region Registering extra temporary files

        /// <summary>
        /// Add a file or directory to be managed by this <see cref="TemporaryFileManager"/> instance. It will be deleted on disposal
        /// of this instance.
        /// </summary>
        /// <param name="path">The absolute path to be managed by this instance</param>
        /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist as a file or directory</exception>
        public void RegisterFileOrDirectory(string path)
        {
            lock (m_Lock)
            {
                if (Directory.Exists(path))
                {
                    m_Directories[path] = true;
                }
                else if (File.Exists(path))
                {
                    m_Files[path] = true;
                }
            }
        }

        public void RegisterDirectory(string path)
        {
            lock (m_Lock)
            {
                m_Directories[path] = true;
            }
        }

        public void RegisterFile(string path)
        {
            lock (m_Lock)
            {
                m_Files[path] = true;
            }
        }

        #endregion

        #region Deleting temporary files & directories

        public bool IsFileManaged(string filename)
        {
            return m_Files.ContainsKey(filename);
        }

        /// <summary>
        /// Deletes the specified temporary file.
        /// </summary>
        /// <param name="filename">The path of the temporary file to delete.
        /// This must have been returned by a previous call to <em>GetNewTempFile</em>.</param>
        /// <returns><em>True</em> if the file was successfully deleted. <em>False</em> if the file is currently in use.</returns>
        /// <exception cref="ArgumentException"><paramref name="filename"/> is not registered as a temporary file.</exception>
        public bool DeleteFile(string filename)
        {
            lock (m_Lock)
            {
                if (!m_Files.ContainsKey(filename))
                {
                    return false;
                }

                File.SetAttributes(filename, FileAttributes.Normal);
                try
                {
                    File.Delete(filename);
                    m_Files.Remove(filename);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Deletes the specified temporary directory and all its contents.
        /// </summary>
        /// <param name="dirname">The path of the temporary directory to delete.
        /// This must have been returned by a previous call to <em>GetNewTempDirectory</em>.</param>
        /// <returns><em>True</em> if the directory was successfully deleted. <em>False</em> if the directory is currently in use.</returns>
        /// <exception cref="UnauthorizedAccessException">You do not have write permission to the directory.</exception>
        public bool DeleteDirectory(string dirname)
        {
            lock (m_Lock)
            {
                if (!m_Directories.ContainsKey(dirname))
                {
                    return false;
                }

                try
                {
                    Directory.Delete(dirname, true);
                    m_Directories.Remove(dirname);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }

        #endregion

        #region IDisposable

        protected void Dispose(bool disposing)
        {
            lock (m_Lock)
            {
                if (!IsDisposed)
                {
                    m_Disposed = true;
                    // delete files we've been told to delete
                    // these are unmanaged resources, so delete the files wherever we're called from
                    foreach (KeyValuePair<string, bool> kvp in m_Files)
                    {
                        try
                        {
                            if (kvp.Value && File.Exists(kvp.Key))
                            {
                                File.Delete(kvp.Key);
                            }
                        }
                        catch
                        {
                        } // don't care anymore
                    }

                    //delete directories we've been told to delete
                    foreach (KeyValuePair<string, bool> kvp in m_Directories)
                    {
                        try
                        {
                            if (kvp.Value && Directory.Exists(kvp.Key))
                            {
                                Directory.Delete(kvp.Key, true);
                            }
                        }
                        catch
                        {
                        } // don't care anymore
                    }
                }
            }
        }

        #endregion
    }
}
