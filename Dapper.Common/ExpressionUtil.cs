﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Linq.Expressions;

namespace Dapper.Extension.Util
{
    public class ExpressionUtil : ExpressionVisitor
    {
        #region propertys
        private StringBuilder _build = new StringBuilder();
        private Dictionary<string, object> _param { get; set; }
        private string _paramName = "Name";
        private string _operator { get; set; }
        private bool _singleTable { get; set; }
        #endregion

        #region override
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && node.Expression.NodeType == ExpressionType.Parameter)
            {
                SetName(node);
            }
            else
            {
                SetValue(node);
            }
            return node;
        }
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(ExtensionUtil))
            {
                _build.Append("(");
                if (node.Arguments.Count == 1)
                {
                    Visit(node.Arguments[0]);
                    _build.AppendFormat(" {0} ", ExtensionUtil.GetOperator(node.Method.Name));
                }
                else if (node.Arguments.Count == 2)
                {
                    Visit(node.Arguments[0]);
                    _operator = ExtensionUtil.GetOperator(node.Method.Name);
                    _build.AppendFormat(" {0} ", _operator);
                    Visit(node.Arguments[1]);
                }
                else
                {
                    _operator = ExtensionUtil.GetOperator(node.Method.Name);
                    Visit(node.Arguments[0]);
                    _build.AppendFormat(" {0} ", _operator);
                    Visit(node.Arguments[1]);
                    _build.AppendFormat(" {0} ", ExtensionUtil.GetOperator(ExpressionType.AndAlso));
                    Visit(node.Arguments[2]);
                }
                _build.Append(")");
            }
            else if (node.Method.GetCustomAttributes(typeof(FunctionAttribute), true).Length > 0)
            {
                _build.AppendFormat("{0}(", node.Method.Name.ToUpper());
                var parameters = node.Method.GetParameters();
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    if (parameters[i].GetCustomAttributes(typeof(ParameterAttribute), true).Length > 0)
                    {
                        _build.Append((node.Arguments[i] as ConstantExpression).Value);
                        continue;
                    }
                    Visit(node.Arguments[i]);
                    if (i + 1 != node.Arguments.Count)
                    {
                        _build.Append(",");
                    }
                }
                _build.Append(")");
            }
            else
            {
                SetValue(node);
            }
            return node;
        }
        protected override Expression VisitBinary(BinaryExpression node)
        {
            _build.Append("(");
            Visit(node.Left);
            if (node.Right is ConstantExpression && (node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual) && (node.Right as ConstantExpression).Value == null)
            {
                _operator = node.NodeType == ExpressionType.Equal ? ExtensionUtil.GetOperator(nameof(ExtensionUtil.IsNull)) : ExtensionUtil.GetOperator(nameof(ExtensionUtil.IsNotNull));
                _build.AppendFormat(" {0}", _operator);
            }
            else
            {
                _operator = ExtensionUtil.GetOperator(node.NodeType);
                _build.AppendFormat(" {0} ", _operator);
                Visit(node.Right);
            }
            _build.Append(")");
            return node;
        }
        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            SetValue(node);
            return node;
        }
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                _build.AppendFormat("{0}", ExtensionUtil.GetOperator(ExpressionType.Not));
            }
            Visit(node.Operand);
            return node;
        }
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _build.AppendFormat("NULL");
            }
            else if (node.Value is string)
            {
                _build.AppendFormat("'{0}'", node.Value);
            }
            else
            {
                _build.AppendFormat("{0}", node.Value);
            }
            return node;
        }
        #endregion

        #region private
        public void SetName(MemberExpression expression)
        {
            var name = expression.Member.Name;
            var columnName = EntityUtil.GetColumn(expression.Expression.Type, f => f.CSharpName == name)?.ColumnName ?? name;
            if (!_singleTable)
            {
                var tableName = EntityUtil.GetTable(expression.Expression.Type).TableName;
                columnName = string.Format("{0}.{1}", tableName, columnName);
            }
            _build.Append(columnName);
            _paramName = name;
        }
        public void SetValue(Expression expression)
        {
            var value = GetValue(expression);
            if (_operator == "LIKE" || _operator == "NOT LIKE")
            {
                value = string.Format("%{0}%", value);
            }
            var key = string.Format("@{0}{1}", _paramName, _param.Count);
            _param.Add(key, value);
            _build.Append(key);
        }
        #endregion

        #region public
        public static string BuildExpression(Expression expression, Dictionary<string, object> param, bool singleTable = true)
        {
            var visitor = new ExpressionUtil
            {
                _param = param,
                _singleTable = singleTable,
            };
            visitor.Visit(expression);
            return visitor._build.ToString();
        }
        public static Dictionary<string, string> BuildColumns(Expression expression, Dictionary<string, object> param, bool singleTable = true)
        {
            var columns = new Dictionary<string, string>();
            if (expression is LambdaExpression)
            {
                expression = (expression as LambdaExpression).Body;
            }
            if (expression is MemberInitExpression)
            {
                var initExpression = (expression as MemberInitExpression);
                for (int i = 0; i < initExpression.Bindings.Count; i++)
                {
                    var column = string.Empty;
                    Expression argument = (initExpression.Bindings[i] as MemberAssignment).Expression;
                    if (argument is UnaryExpression)
                    {
                        argument = (argument as UnaryExpression).Operand;
                    }
                    if (argument is MethodCallExpression && (argument as MethodCallExpression).Method.DeclaringType == typeof(Convert))
                    {
                        column = GetValue((argument as MethodCallExpression).Arguments[0]).ToString();
                    }
                    else if (argument is ConstantExpression || (argument is MemberExpression && (argument as MemberExpression).Expression is ConstantExpression))
                    {
                        column = GetValue(argument).ToString();
                    }
                    else
                    {
                        column = BuildExpression(argument, param, singleTable);
                    }
                    var name = initExpression.Bindings[i].Member.Name;
                    columns.Add(name, column);
                }
            }
            else if (expression is NewExpression)
            {
                var newExpression = (expression as NewExpression);
                for (int i = 0; i < newExpression.Arguments.Count; i++)
                {
                    var columnName = string.Empty;
                    var argument = newExpression.Arguments[i];
                    if (argument is MethodCallExpression && (argument as MethodCallExpression).Method.DeclaringType == typeof(Convert))
                    {
                        columnName = GetValue((argument as MethodCallExpression).Arguments[0]).ToString();
                    }
                    else if (argument is ConstantExpression || (argument is MemberExpression && (argument as MemberExpression).Expression is ConstantExpression))
                    {
                        columnName = GetValue(argument).ToString();
                    }
                    else
                    {
                        columnName = BuildExpression(argument, param,singleTable);
                    }
                    var name = newExpression.Members[i].Name;
                    columns.Add(name, columnName);
                }
            }
            else if (expression is MemberExpression)
            {
                var memberExpression = (expression as MemberExpression);
                var name = memberExpression.Member.Name;
                var columnName = EntityUtil.GetColumn(memberExpression.Expression.Type, f => f.CSharpName == name)?.ColumnName ?? name;
                if (!singleTable)
                {
                    var tableName = EntityUtil.GetTable(memberExpression.Expression.Type).TableName;
                    columnName = string.Format("{0}.{1}", tableName, columnName);
                }
                columns.Add(name, columnName);
            }
            else
            {
                var name = string.Format("COLUMN0");
                var columnName = BuildExpression(expression, param, singleTable);
                columns.Add(name, columnName);
            }
            return columns;
        }
        public static Dictionary<string, string> BuildColumn(Expression expression, Dictionary<string, object> param, bool singleTable = true)
        {
            if (expression is LambdaExpression)
            {
                expression = (expression as LambdaExpression).Body;
            }
            var column = new Dictionary<string, string>();
            if (expression is MemberExpression)
            {
                var memberExpression = (expression as MemberExpression);
                var name = memberExpression.Member.Name;
                var columnName = EntityUtil.GetColumn(memberExpression.Expression.Type, f => f.CSharpName == name)?.ColumnName ?? name;
                if (!singleTable)
                {
                    var tableName = EntityUtil.GetTable(memberExpression.Expression.Type).TableName;
                    columnName = string.Format("{0}.{1}", tableName, columnName);
                }
                column.Add(name, columnName);
                return column;
            }
            else
            {
                var name = string.Format("COLUMN0");
                var build = BuildExpression(expression, param, singleTable);
                column.Add(name, build);
                return column;
            }
        }
        public static object GetValue(Expression expression)
        {
            var memberNames = new Stack<string>();
            var tempExpression = expression;
            while (tempExpression is MemberExpression)
            {
                var memberExpression = tempExpression as MemberExpression;
                memberNames.Push(memberExpression.Member.Name);
                if (tempExpression == null && memberExpression.Type == typeof(DateTime) && memberExpression.Member.Name == nameof(DateTime.Now))
                {
                    return DateTime.Now;
                }
                tempExpression = memberExpression.Expression;
            }

            if (tempExpression is ConstantExpression)
            {
                var value = (tempExpression as ConstantExpression).Value;
                foreach (var item in memberNames)
                {
                    if (value.GetType().GetField(item) != null)
                    {
                        value = value.GetType().GetField(item).GetValue(value);
                    }
                    else if(value.GetType().GetProperty(item)!=null)
                    {
                        value = value.GetType().GetProperty(item).GetValue(value);
                    }
                    else
                    {
                        return value;
                    }
                }
                return value;
            }
            else
            {
                return Expression.Lambda(expression).Compile().DynamicInvoke();
            }
        }
        #endregion
    }
}
