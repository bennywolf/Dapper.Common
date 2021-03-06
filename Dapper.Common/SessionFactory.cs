﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Linq;
using static Dapper.SqlMapper;
using System.Diagnostics;

namespace Dapper.Extension
{
    public interface ISession : IDisposable
    {
        bool? Buffered { get; set; }
        int? Timeout { get; set; }
        List<Logger> Loggers { get; }
        IQueryable<T> From<T>() where T : class;
        IQueryable<T1, T2> From<T1, T2>() where T1 : class where T2 : class;
        IQueryable<T1, T2, T3> From<T1, T2, T3>() where T1 : class where T2 : class where T3 : class;
        GridReader QueryMultiple(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text);
        int Execute(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text);
        T ExecuteScalar<T>(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text);
        IEnumerable<T> Query<T>(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null);
        IEnumerable<dynamic> Query(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null);
        IDbConnection Connection { get; }
        IDbTransaction Transaction { get; }
        DataSourceType SourceType { get; }
        SessionState State { get; }
        void Open(bool beginTransaction, IsolationLevel? level = null);
        void Commit();
        void Rollback();
        void Close();
    }
    internal class Session : ISession
    {
        public DataSourceType SourceType { get; private set; }
        public List<Logger> Loggers { get; private set; }
        public IDbTransaction Transaction { get; private set; }
        public IDbConnection Connection { get; }
        public bool? Buffered { get; set; }
        public int? Timeout { get; set; }
        public SessionState State { get; private set; }
        public Session(IDbConnection connection, DataSourceType sourceType)
        {
            Connection = connection;
            SourceType = sourceType;
            State = SessionState.Closed;
        }
        public void Open(bool beginTransaction, IsolationLevel? level = null)
        {
            if (!beginTransaction)
            {
                Connection.Open();
            }
            else
            {
                Connection.Open();
                Transaction = level == null ? Connection.BeginTransaction() : Connection.BeginTransaction(level.Value);
            }
            State = SessionState.Open;
        }
        public void Close()
        {
            Connection.Close();
            State = SessionState.Closed;
        }
        public void Commit()
        {
            if (Transaction != null)
            {
                Transaction.Commit();
                Transaction.Dispose();
                State = SessionState.Commit;
            }
        }
        public void Rollback()
        {
            if (Transaction != null)
            {
                Transaction.Rollback();
                Transaction.Dispose();
                State = SessionState.Rollback;
            }
        }
        public void Dispose()
        {
            Close();
        }
        public GridReader QueryMultiple(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            return Connection.QueryMultiple(sql, param, Transaction, Timeout != null ? Timeout.Value : commandTimeout, text);
        }
        public int Execute(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            return Connection.Execute(sql, param, Transaction, Timeout != null ? Timeout.Value : commandTimeout, text);
        }
        public T ExecuteScalar<T>(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            return Connection.ExecuteScalar<T>(sql, param, Transaction, Timeout != null ? Timeout.Value : commandTimeout, text);
        }
        public IQueryable<T> From<T>() where T : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T>(this);
            }
            throw new NotImplementedException();
        }
        public IQueryable<T1, T2> From<T1, T2>() where T1 : class where T2 : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T1, T2>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T1, T2>(this);
            }
            throw new NotImplementedException();
        }
        public IQueryable<T1, T2, T3> From<T1, T2, T3>() where T1 : class where T2 : class where T3 : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T1, T2, T3>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T1, T2, T3>(this);
            }
            throw new NotImplementedException();
        }
        public IEnumerable<T> Query<T>(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null)
        {
            return Connection.Query<T>(sql, param, Transaction, Buffered != null ? Buffered.Value : buffered, Timeout != null ? Timeout.Value : commandTimeout, commandType);
        }
        public IEnumerable<dynamic> Query(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null)
        {
            return Connection.Query(sql, param, Transaction, Buffered != null ? Buffered.Value : buffered, Timeout != null ? Timeout.Value : commandTimeout, commandType);
        }

    }
    internal class SessionProxy : ISession
    {
        private ISession _target = null;
        public SessionProxy(ISession target)
        {
            _target = target;
            Loggers = new List<Logger>();
        }
        public List<Logger> Loggers { get; private set; }

        public IDbConnection Connection => _target.Connection;

        public IDbTransaction Transaction => _target.Transaction;

        public SessionState State => _target.State;

        public DataSourceType SourceType => _target.SourceType;

        public bool? Buffered { get => _target.Buffered; set => _target.Buffered = value; }
        public int? Timeout { get => _target.Timeout; set => _target.Timeout = value; }

        public void Close()
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                _target.Close();
                watch.Stop();
            }
            finally
            {
                Loggers.Add(new Logger()
                {
                    Value = null,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    Text = nameof(SessionProxy.Close),
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public void Commit()
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                _target.Commit();
                watch.Stop();
            }
            finally
            {
                Loggers.Add(new Logger()
                {
                    Value = null,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    Text = nameof(Commit),
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public void Dispose()
        {
            _target.Dispose();
        }

        public int Execute(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                return _target.Execute(sql, param, commandTimeout, text);
            }
            finally
            {
                watch.Stop();
                Loggers.Add(new Logger()
                {
                    Value = param,
                    Text = sql,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public T ExecuteScalar<T>(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                return _target.ExecuteScalar<T>(sql, param, commandTimeout, text);
            }
            finally
            {
                watch.Stop();
                Loggers.Add(new Logger()
                {
                    Value = param,
                    Text = sql,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public IQueryable<T> From<T>() where T : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T>(this);
            }
            throw new NotImplementedException();
        }
        public IQueryable<T1, T2> From<T1, T2>() where T1 : class where T2 : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T1, T2>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T1, T2>(this);
            }
            throw new NotImplementedException();
        }
        public IQueryable<T1, T2, T3> From<T1, T2, T3>() where T1 : class where T2 : class where T3 : class
        {
            if (SourceType == DataSourceType.MYSQL)
            {
                return new MysqlQuery<T1, T2, T3>(this);
            }
            if (SourceType == DataSourceType.SQLSERVER)
            {
                return new SqlQuery<T1, T2, T3>(this);
            }
            throw new NotImplementedException();
        }

        public void Open(bool beginTransaction, IsolationLevel? level = null)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                _target.Open(beginTransaction, level);
                watch.Stop();
            }
            finally
            {
                Loggers.Add(new Logger()
                {
                    Value = null,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    Text = string.Format("{0}:Transaction:{1}", nameof(Open), beginTransaction ? "ON" : "OFF"),
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public IEnumerable<T> Query<T>(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                return _target.Query<T>(sql, param, buffered, commandTimeout, commandType);
            }
            finally
            {
                watch.Stop();
                Loggers.Add(new Logger()
                {
                    Value = param,
                    Text = sql,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public IEnumerable<dynamic> Query(string sql, object param = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                return _target.Query(sql, param, buffered, commandTimeout, commandType);

            }
            finally
            {
                watch.Stop();
                Loggers.Add(new Logger()
                {
                    Value = param,
                    Text = sql,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public GridReader QueryMultiple(string sql, object param = null, int? commandTimeout = null, CommandType text = CommandType.Text)
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                return _target.QueryMultiple(sql, param, commandTimeout, text);
            }
            finally
            {
                watch.Stop();
                Loggers.Add(new Logger()
                {
                    Value = param,
                    Text = sql,
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }

        public void Rollback()
        {
            var watch = new Stopwatch();
            try
            {
                watch.Start();
                _target.Rollback();
                watch.Stop();
            }
            finally
            {
                Loggers.Add(new Logger()
                {
                    Value = null,
                    Text = nameof(Rollback),
                    Buffered = Buffered,
                    Timeout = Timeout,
                    ExecuteTime = watch.ElapsedMilliseconds
                });
            }
        }
    }
    public enum SessionState
    {
        Closed = 0,
        Open = 1,
        Commit = 2,
        Rollback = 3,
    }
    public class SessionFactory
    {
        static SessionFactory()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
        }
        private static List<DataSource> DataSource = new List<DataSource>();
        public static DataSource GetDataSource(string name = null)
        {
            return name == null ? DataSource.FirstOrDefault() : DataSource.Find(f => f.Name == name);
        }
        public static void AddDataSource(DataSource dataSource)
        {
            DataSource.Add(dataSource);
        }
        public static ISession GetSession(string name = null)
        {
            var datasource = GetDataSource(name);
            ISession session = null;
            if (datasource.UseProxy)
            {
                session = new SessionProxy(new Session(datasource.Source(), datasource.SourceType));
            }
            else
            {
                session = new Session(datasource.Source(), datasource.SourceType);
            }
            return session;
        }
    }
    public class DataSource
    {
        public Func<IDbConnection> Source { get; set; }
        public DataSourceType SourceType { get; set; }
        public string Name { get; set; }
        public bool UseProxy { get; set; }
    }
    public enum DataSourceType
    {
        MYSQL,
        SQLSERVER,
        ORACLE
    }
}
