﻿//*********************************************************//
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software 
//    distributed under the License is distributed on an "AS IS" BASIS, 
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or 
//    implied. See the License for the specific language governing 
//    permissions and limitations under the License.
//
//*********************************************************//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.NodejsTools.Analysis.Values;
using Microsoft.NodejsTools.Parsing;


namespace Microsoft.NodejsTools.Analysis.Analyzer {
    internal class ExpressionEvaluator {
        private readonly AnalysisUnit _unit;

        internal static readonly IAnalysisSet[] EmptySets = new IAnalysisSet[0];
        internal static readonly Lookup[] EmptyNames = new Lookup[0];

        /// <summary>
        /// Creates a new ExpressionEvaluator that will evaluate in the context of the top-level module.
        /// </summary>
        public ExpressionEvaluator(AnalysisUnit unit) {
            _unit = unit;
            Scope = unit.Environment;
        }

        public ExpressionEvaluator(AnalysisUnit unit, EnvironmentRecord scope) {
            _unit = unit;
            Scope = scope;
        }

        #region Public APIs

        /// <summary>
        /// Returns possible variable refs associated with the expr in the expression evaluators scope.
        /// </summary>
        public IAnalysisSet Evaluate(Expression node) {
            var res = EvaluateWorker(node);
            Debug.Assert(res != null);
            return res;
        }

        public IAnalysisSet EvaluateMaybeNull(Expression node) {
            if (node == null) {
                return null;
            }

            return Evaluate(node);
        }

        /// <summary>
        /// Returns a sequence of possible types associated with the name in the expression evaluators scope.
        /// </summary>
        public IAnalysisSet LookupAnalysisSetByName(Node node, string name, bool addRef = true) {
            foreach (var scope in Scope.EnumerateTowardsGlobal) {
                var refs = scope.GetVariable(node, _unit, name, addRef);
                if (refs != null) {
                    if (addRef) {
                        var linkedVars = scope.GetLinkedVariablesNoCreate(name);
                        if (linkedVars != null) {
                            foreach (var linkedVar in linkedVars) {
                                linkedVar.AddReference(node, _unit);
                            }
                        }
                    }
                    return refs.GetTypes(_unit);
                }
            }

            return ProjectState._globalObject.Get(node, _unit, name);
        }

        #endregion

        #region Implementation Details

        private JsAnalyzer ProjectState {
            get { return _unit.Analyzer; }
        }

        /// <summary>
        /// The list of scopes which define the current context.
        /// </summary>
#if DEBUG
        public EnvironmentRecord Scope {
            get { return _currentScope; }
            set {
                // Scopes must be from a common stack.
                Debug.Assert(_currentScope == null ||
                    _currentScope.Parent == value.Parent ||
                    _currentScope.EnumerateTowardsGlobal.Contains(value) ||
                    value.EnumerateTowardsGlobal.Contains(_currentScope));
                _currentScope = value;
            }
        }

        private EnvironmentRecord _currentScope;
#else
        public EnvironmentRecord Scope;
#endif

        private IAnalysisSet EvaluateWorker(Node node) {
            EvalDelegate eval;
            if (_evaluators.TryGetValue(node.GetType(), out eval)) {
                return eval(this, node);
            }

            return AnalysisSet.Empty;
        }

        delegate IAnalysisSet EvalDelegate(ExpressionEvaluator ee, Node node);

        private static Dictionary<Type, EvalDelegate> _evaluators = new Dictionary<Type, EvalDelegate> {
            { typeof(BinaryOperator), ExpressionEvaluator.EvaluateBinary },
            { typeof(CallNode), ExpressionEvaluator.EvaluateCall },
            { typeof(Conditional), ExpressionEvaluator.EvaluateConditional},
            { typeof(ConstantWrapper), ExpressionEvaluator.EvaluateConstant },
            { typeof(DirectivePrologue), ExpressionEvaluator.EvaluateConstant },
            { typeof(ObjectLiteral), ExpressionEvaluator.EvaluateObjectLiteral },
            { typeof(Member), ExpressionEvaluator.EvaluateMember },
            { typeof(Lookup), ExpressionEvaluator.EvaluateLookup },
            { typeof(GroupingOperator), ExpressionEvaluator.EvaluateGroupingOperator },
            { typeof(UnaryOperator), ExpressionEvaluator.EvaluateUnary },
            { typeof(ArrayLiteral), ExpressionEvaluator.EvaluateArrayLiteral },
            { typeof(FunctionExpression), ExpressionEvaluator.EvaluateFunctionExpression },
            { typeof(ThisLiteral), ExpressionEvaluator.EvaluateThis }
#if FALSE
            { typeof(YieldExpression), ExpressionEvaluator.EvaluateYield },
            { typeof(YieldFromExpression), ExpressionEvaluator.EvaluateYieldFrom },
#endif
        };

        private static IAnalysisSet EvaluateThis(ExpressionEvaluator ee, Node node) {
            return ee.Scope.ThisValue;
        }

        private static IAnalysisSet EvaluateArrayLiteral(ExpressionEvaluator ee, Node node) {
            return ee.MakeArrayValue(ee, node);
        }

        private static IAnalysisSet EvaluateGroupingOperator(ExpressionEvaluator ee, Node node) {
            var n = (GroupingOperator)node;
            if (n.Operand == null) {
                // can happen with invalid code such as var x = ()
                return AnalysisSet.Empty;
            }
            return ee.Evaluate(n.Operand);
        }

        private static IAnalysisSet EvaluateLookup(ExpressionEvaluator ee, Node node) {
            var n = (Lookup)node;
            var res = ee.LookupAnalysisSetByName(node, n.Name);
            foreach (var value in res) {
                value.Value.AddReference(node, ee._unit);
            }
            return res;
        }

        private static IAnalysisSet EvaluateMember(ExpressionEvaluator ee, Node node) {
            var n = (Member)node;
            return ee.Evaluate(n.Root).Get(node, ee._unit, n.Name);
        }

        private static IAnalysisSet EvaluateIndex(ExpressionEvaluator ee, Node node) {
            var n = (CallNode)node;

            var indexArg = GetIndexArgument(n);
            if (indexArg != null) {
                return ee.Evaluate(n.Function).GetIndex(n, ee._unit, ee.Evaluate(indexArg));
            }
            return AnalysisSet.Empty;
        }

        private static Expression GetIndexArgument(CallNode n) {
            if (n.Arguments.Length > 0) {
                Debug.Assert(n.Arguments.Length == 1);
                var comma = n.Arguments[0] as CommaOperator;
                if (comma != null) {
                    return comma.Expressions[comma.Expressions.Length - 1];
                }
                return n.Arguments[0];
            }
            return null;
        }

        private static IAnalysisSet EvaluateObjectLiteral(ExpressionEvaluator ee, Node node) {

            var n = (ObjectLiteral)node;
            IAnalysisSet value;
            if (ee.Scope.GlobalEnvironment.TryGetNodeValue(
                NodeEnvironmentKind.ObjectLiteralValue,
                node,
                out value)) {
                var objectInfo = (ObjectLiteralValue)value.First().Value;
                int maxProperties = ee._unit.Analyzer.Limits.MaxObjectLiteralProperties;
                if (n.Properties.Length > maxProperties) {
                    int nonFunctionCount = 0;
                    foreach (var prop in n.Properties) {
                        FunctionExpression func = prop.Value as FunctionExpression;
                        if (func == null) {
                            nonFunctionCount++;
                        }
                    }

                    if (nonFunctionCount > maxProperties) {
                        // probably some generated object literal, ignore it
                        // for the post part.
                        AssignProperty(ee, node, objectInfo, n.Properties.First());
                    } else {
                        foreach (var x in n.Properties) {
                            AssignProperty(ee, node, objectInfo, x);
                        }
                    }
                } else {
                    foreach (var x in n.Properties) {
                        AssignProperty(ee, node, objectInfo, x);
                    }
                }

                return value;
            }
            Debug.Fail("Failed to find object literal value");
            return AnalysisSet.Empty;
        }

        private static void AssignProperty(ExpressionEvaluator ee, Node node, ObjectLiteralValue objectInfo, ObjectLiteralProperty x) {
            if (x.Name.Value is string) {
                objectInfo.SetMember(
                    node,
                    ee._unit,
                    (string)x.Name.Value,
                    ee.EvaluateMaybeNull(x.Value) ?? AnalysisSet.Empty
                );
            } else {
                // {42:42}
                objectInfo.SetIndex(
                    node,
                    ee._unit,
                    ee.ProjectState.GetConstant(x.Name.Value).Proxy ?? AnalysisSet.Empty,
                    ee.EvaluateMaybeNull(x.Value) ?? AnalysisSet.Empty
                );
            }
        }

        private static IAnalysisSet EvaluateConstant(ExpressionEvaluator ee, Node node) {
            var n = (ConstantWrapper)node;

            return ee.ProjectState.GetConstant(n.Value).Proxy;
        }

        private static IAnalysisSet EvaluateConditional(ExpressionEvaluator ee, Node node) {
            var n = (Conditional)node;
            ee.Evaluate(n.Condition);
            var result = ee.Evaluate(n.TrueExpression);
            return result.Union(ee.Evaluate(n.FalseExpression));
        }

        private static IAnalysisSet EvaluateCall(ExpressionEvaluator ee, Node node) {
            var n = (CallNode)node;
            if (n.InBrackets) {
                return EvaluateIndex(ee, node);
            }

            // Get the argument types that we're providing at this call site
            var argTypes = ee.Evaluate(n.Arguments);

            // Then lookup the possible methods we're calling


            var res = AnalysisSet.Empty;
            if (n.IsConstructor) {
                var targetRefs = ee.Evaluate(n.Function);
                foreach (var target in targetRefs) {
                    res = res.Union(target.Value.Construct(node, ee._unit, argTypes));
                }
            } else {
                IAnalysisSet @this;
                IAnalysisSet targetRefs = ee.EvaluateReference(node, n, out @this);

                foreach (var target in targetRefs) {
                    res = res.Union(target.Call(node, ee._unit, @this, argTypes));
                }
            }
            return res;
        }

        /// <summary>
        /// Evaluates strings and produces the combined literal value.  We usually cannot do this
        /// because we need to make sure that we're not constantly introducing new string literals
        /// into the evaluation system.  Otherwise a recursive concatencating function could
        /// cause an infinite analysis.  But in special situations, such as analyzing require calls,
        /// where we know the value doesn't get introduced into the analysis system we want to get the 
        /// fully evaluated string literals.
        /// </summary>
        public IEnumerable<string> MergeStringLiterals(Expression node) {
            BinaryOperator binOp = node as BinaryOperator;
            if (binOp != null && binOp.OperatorToken == JSToken.Plus) {
                foreach (var left in MergeStringLiterals(binOp.Operand1)) {
                    foreach (var right in MergeStringLiterals(binOp.Operand2)) {
                        yield return left + right;
                    }
                }
                yield break;
            }
            CallNode call = node as CallNode;
            if (call != null && call.Arguments.Length == 2) {
                var func = EvaluateMaybeNull(call.Function);
                foreach (var targetFunc in func) {
                    BuiltinFunctionValue bf = targetFunc.Value as BuiltinFunctionValue;
                    if (bf != null &&
                        bf.DeclaringModule.ModuleName == "path" && 
                        bf.Name == "join") {
                        foreach (var left in MergeStringLiterals(call.Arguments[0])) {
                            foreach (var right in MergeStringLiterals(call.Arguments[1])) {
                                yield return left + right;
                            }
                        }
                    }
                }
            }
            foreach (var value in EvaluateMaybeNull(node)) {
                var strValue = value.Value.GetStringValue();
                if (strValue != null) {
                    yield return strValue;
                }
            }
        }

        private IAnalysisSet EvaluateReference(Node node, CallNode n, out IAnalysisSet baseValue) {
            Member member = n.Function as Member;
            IAnalysisSet targetRefs;
            if (member != null) {
                baseValue = Evaluate(member.Root);
                @targetRefs = baseValue.Get(node, _unit, member.Name);
            } else {
                CallNode call = n.Function as CallNode;
                if (call != null && call.InBrackets && call.Arguments.Length == 1) {
                    baseValue = Evaluate(call.Arguments[0]);
                    targetRefs = Evaluate(call.Function);
                } else {
                    baseValue = null;
                    targetRefs = Evaluate(n.Function);
                }
            }
            return targetRefs;
        }

        private IAnalysisSet[] Evaluate(Expression[] astNodeList) {
            var res = new IAnalysisSet[astNodeList.Length];
            for (int i = 0; i < res.Length; i++) {
                res[i] = Evaluate(astNodeList[i]);
            }
            return res;
        }

        private static IAnalysisSet EvaluateUnary(ExpressionEvaluator ee, Node node) {
            var n = (UnaryOperator)node;
            var operand = ee.Evaluate(n.Operand);
            switch (n.OperatorToken) {
                case JSToken.TypeOf:
                    IAnalysisSet res = AnalysisSet.Empty;
                    foreach (var expr in operand) {
                        string typeName;
                        switch (expr.Value.TypeId) {
                            case BuiltinTypeId.Function: typeName = "function"; break;
                            case BuiltinTypeId.String: typeName = "string"; break;
                            case BuiltinTypeId.Null: typeName = "null"; break;
                            case BuiltinTypeId.Undefined: typeName = "undefined"; break;
                            case BuiltinTypeId.Number: typeName = "number"; break;
                            case BuiltinTypeId.Boolean: typeName = "boolean"; break;
                            default: typeName = "object"; break;
                        }
                        res = res.Union(ee.ProjectState.GetConstant(typeName).Proxy);
                    }
                    return res;
                case JSToken.Void:
                    return ee._unit.Analyzer._undefined.Proxy;
            }

            return operand.UnaryOperation(node, ee._unit, n.OperatorToken);
        }

        private static IAnalysisSet EvaluateBinary(ExpressionEvaluator ee, Node node) {
            var n = (BinaryOperator)node;
            switch (n.OperatorToken) {
                case JSToken.LogicalAnd:
                case JSToken.LogicalOr:
                    var result = ee.Evaluate(n.Operand1);
                    return result.Union(ee.Evaluate(n.Operand2));
                case JSToken.PlusAssign:                     // +=
                case JSToken.MinusAssign:                    // -=
                case JSToken.MultiplyAssign:                 // *=
                case JSToken.DivideAssign:                   // /=
                case JSToken.ModuloAssign:                   // %=
                case JSToken.BitwiseAndAssign:               // &=
                case JSToken.BitwiseOrAssign:                // |=
                case JSToken.BitwiseXorAssign:               // ^=
                case JSToken.LeftShiftAssign:                // <<=
                case JSToken.RightShiftAssign:               // >>=
                case JSToken.UnsignedRightShiftAssign:       // >>>=
                    var rightValue = ee.Evaluate(n.Operand2);
                    foreach (var x in ee.Evaluate(n.Operand1)) {
                        x.AugmentAssign(n, ee._unit, rightValue);
                    }
                    return rightValue;
                case JSToken.Assign:
                    EnvironmentRecord newEnv;
                    if (ee.Scope.GlobalEnvironment.TryGetNodeEnvironment(n, out newEnv)) {
                        var res = ee.Evaluate(n.Operand2);
                        ee.Scope = newEnv;
                        ee.AssignTo(n, n.Operand1, res);
                        return res;
                    } else {
                        var rhs = ee.Evaluate(n.Operand2);
                        ee.AssignTo(n, n.Operand1, rhs);
                        return rhs;
                    }
            }

            return ee.Evaluate(n.Operand1)
                .BinaryOperation(n, ee._unit, ee.Evaluate(n.Operand2));
        }

#if FALSE
        private static IAnalysisSet EvaluateYield(ExpressionEvaluator ee, Node node) {
            var yield = (YieldExpression)node;
            var scope = ee.Scope as FunctionScope;
            if (scope != null && scope.Generator != null) {
                var gen = scope.Generator;
                var res = ee.Evaluate(yield.Expression);

                gen.AddYield(node, ee._unit, res);

                gen.Sends.AddDependency(ee._unit);
                return gen.Sends.Types;
            }
            return AnalysisSet.Empty;
        }

        private static IAnalysisSet EvaluateYieldFrom(ExpressionEvaluator ee, Node node) {
            var yield = (YieldFromExpression)node;
            var scope = ee.Scope as FunctionScope;
            if (scope != null && scope.Generator != null) {
                var gen = scope.Generator;
                var res = ee.Evaluate(yield.Expression);

                gen.AddYieldFrom(node, ee._unit, res);

                gen.Returns.AddDependency(ee._unit);
                return gen.Returns.Types;
            }

            return AnalysisSet.Empty;
        }
#endif

        internal void AssignTo(Node assignStmt, Expression left, IAnalysisSet values) {
            if (left is Lookup) {
                var l = (Lookup)left;
                if (l.Name != null) {
                    var assignScope = Scope;
                    if (l.VariableField != null) {
                        foreach (var scope in Scope.EnumerateTowardsGlobal) {
                            if(scope.ContainsVariable(l.Name) ||
                                (scope is DeclarativeEnvironmentRecord &&
                                ((DeclarativeEnvironmentRecord)scope).Node == l.VariableField.Scope)) {
                                assignScope = scope;
                                break;
                            }
                        }
                    }

                    assignScope.AssignVariable(
                        l.Name,
                        l,
                        _unit,
                        values
                    );
                }
            } else if (left is Member) {
                var l = (Member)left;
                if (l.Name != null) {
                    foreach (var obj in Evaluate(l.Root)) {
                        obj.SetMember(l, _unit, l.Name, values);
                    }
                }
            } else if (left is CallNode) {
                var call = (CallNode)left;
                if (call.InBrackets) {
                    var indexObj = Evaluate(call.Function);
                    var indexArg = GetIndexArgument(call);
                    if (indexArg != null) {
                        foreach (var obj in Evaluate(indexArg)) {
                            indexObj.SetIndex(assignStmt, _unit, obj, values);
                        }
                    }
                }
            }
        }

        private static IAnalysisSet EvaluateFunctionExpression(ExpressionEvaluator ee, Node node) {
            var func = (FunctionExpression)node;
            EnvironmentRecord funcRec;
            if (ee.Scope.GlobalEnvironment.TryGetNodeEnvironment(func.Function, out funcRec)) {
                return ((FunctionEnvironmentRecord)funcRec).AnalysisValue.SelfSet;
            }
            
            Debug.Assert(ee._unit.ForEval, "Failed to find function record");
            return AnalysisSet.Empty;
        }

        private IAnalysisSet MakeArrayValue(ExpressionEvaluator ee, Node node) {
            int maxArrayLiterals = _unit.Analyzer.Limits.MaxArrayLiterals;

            IAnalysisSet value;
            var array = (ArrayLiteral)node;
            ArrayValue arrValue;
            if (!ee.Scope.GlobalEnvironment.TryGetNodeValue(NodeEnvironmentKind.ArrayValue, node, out value)) {
                TypedDef[] elements = TypedDef.EmptyArray;

                if (array.Elements.Length > maxArrayLiterals) {
                    // probably some generated object literal, simplify it's analysis
                    elements = new TypedDef[] { new TypedDef() };
                } else if(array.Elements.Length != 0) {
                    elements = TypedDef.Generator.Take(array.Elements.Length).ToArray();
                }

                arrValue = new ArrayValue(
                    elements,
                    _unit.ProjectEntry,
                    node
                );

                ee.Scope.GlobalEnvironment.AddNodeValue(
                    NodeEnvironmentKind.ArrayValue,
                    node,
                    arrValue.SelfSet
                );
            } else {
                arrValue = (ArrayValue)((AnalysisProxy)value).Value;
            }

            for (int i = 0; i < arrValue.IndexTypes.Length; i++) {
                arrValue.AddTypes(
                    ee._unit,
                    i,
                    Evaluate(array.Elements[i])
                );
            }

            return arrValue.SelfSet;
        }

        #endregion
    }
}
