namespace Rhino.Etl.Core.Operations
{
	using System;
	using System.Collections.Generic;
	using System.Configuration;
	using System.Data;
	using System.Data.SqlClient;
	using DataReaders;
	using Rhino.Commons;

	/// <summary>
	/// Allows to execute an operation that perform a bulk insert into a sql server database
	/// </summary>
	public abstract class SqlBulkInsertOperation : AbstractDatabaseOperation
	{
		/// <summary>
		/// The schema of the destination table
		/// </summary>
		private IDictionary<string, Type> _schema = new Dictionary<string, Type>();

		/// <summary>
		/// The mapping of columns from the row to the database schema.
		/// Important: The column name in the database is case sensitive!
		/// </summary>
		public IDictionary<string, string> Mappings = new Dictionary<string, string>();
		private SqlBulkCopy sqlBulkCopy;
		private string targetTable;
		private int timeout;
		private SqlBulkCopyOptions bulkCopyOptions = SqlBulkCopyOptions.Default;

		/// <summary>
		/// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
		/// </summary>
		/// <param name="connectionStringName">Name of the connection string.</param>
		/// <param name="targetTable">The target table.</param>
		protected SqlBulkInsertOperation(string connectionStringName, string targetTable)
			: this(connectionStringName, targetTable, 600)
		{

		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SqlBulkInsertOperation"/> class.
		/// </summary>
		/// <param name="connectionStringName">Name of the connection string.</param>
		/// <param name="targetTable">The target table.</param>
		/// <param name="timeout">The timeout.</param>
		protected SqlBulkInsertOperation(string connectionStringName, string targetTable, int timeout)
			: base(connectionStringName)
		{
			Guard.Against(string.IsNullOrEmpty(targetTable), "TargetTable was not set, but it is mandatory");
			this.targetTable = targetTable;
			this.timeout = timeout;
		}

		/// <summary>The timeout value of the bulk insert operation</summary>
		public virtual int Timeout
		{
			get { return timeout; }
			set { timeout = value; }
		}

		/// <summary>The table or view to bulk load the data into.</summary>
		public string TargetTable
		{
			get { return targetTable; }
			set { targetTable = value; }
		}

		/// <summary><c>true</c> to turn the <see cref="SqlBulkCopyOptions.TableLock"/> option on, otherwise <c>false</c>.</summary>
		public virtual bool LockTable
		{
			get { return IsOptionOn(SqlBulkCopyOptions.TableLock); }
			set { ToggleOption(SqlBulkCopyOptions.TableLock, value); }
		}

		/// <summary>Turns a <see cref="bulkCopyOptions"/> on or off depending on the value of <paramref name="on"/></summary>
		/// <param name="option">The <see cref="SqlBulkCopyOptions"/> to turn on or off.</param>
		/// <param name="on"><c>true</c> to set the <see cref="SqlBulkCopyOptions"/> <paramref name="option"/> on otherwise <c>false</c> to turn the <paramref name="option"/> off.</param>
		protected void ToggleOption(SqlBulkCopyOptions option, bool on)
		{
			if (on)
			{
				TurnOptionOn(option);
			}
			else
			{
				TurnOptionOff(option);
			}
		}

		/// <summary>Returns <c>true</c> if the <paramref name="option"/> is turned on, otherwise <c>false</c></summary>
		/// <param name="option">The <see cref="SqlBulkCopyOptions"/> option to test for.</param>
		/// <returns></returns>
		protected bool IsOptionOn(SqlBulkCopyOptions option)
		{
			return (bulkCopyOptions & option) == option;
		}

		/// <summary>Turns the <paramref name="option"/> on.</summary>
		/// <param name="option"></param>
		protected void TurnOptionOn(SqlBulkCopyOptions option)
		{
			bulkCopyOptions |= option;
		}

		/// <summary>Turns the <paramref name="option"/> off.</summary>
		/// <param name="option"></param>
		protected void TurnOptionOff(SqlBulkCopyOptions option)
		{
			if (IsOptionOn(option))
				bulkCopyOptions ^= option;
		}

		/// <summary>The table or view's schema information.</summary>
		public IDictionary<string, Type> Schema
		{
			get { return _schema; }
			set { _schema = value; }
		}

		/// <summary>
		/// Prepares the mapping for use, by default, it uses the schema mapping.
		/// This is the preferred appraoch
		/// </summary>
		public virtual void PrepareMapping()
		{
			foreach (KeyValuePair<string, Type> pair in _schema)
			{
				Mappings[pair.Key] = pair.Key;
			}
		}

		/// <summary>
		/// Executes this operation
		/// </summary>
		public override IEnumerable<Row> Execute(IEnumerable<Row> rows)
		{
			Guard.Against<ArgumentException>(rows == null, "SqlBulkInsertOperation cannot accept a null enumerator");
			PrepareSchema();
			PrepareMapping();
			using (SqlConnection connection = (SqlConnection)Use.Connection(ConnectionStringName))
			using (SqlTransaction transaction = connection.BeginTransaction())
			{
				sqlBulkCopy = CreateSqlBulkCopy(connection, transaction);
				DictionaryEnumeratorDataReader adapter = new DictionaryEnumeratorDataReader(_schema, rows);
				sqlBulkCopy.WriteToServer(adapter);

				if (PipelineExecuter.HasErrors)
				{
					Warn("Rolling back transaction in {0}", Name);
					transaction.Rollback();
					Warn("Rolled back transaction in {0}", Name);
				}
				else
				{
					Debug("Committing {0}", Name);
					transaction.Commit();
					Debug("Committed {0}", Name);
				}
			}
			yield break;
		}

		/// <summary>
		/// Prepares the schema of the target table
		/// </summary>
		protected abstract void PrepareSchema();

		/// <summary>
		/// Creates the SQL bulk copy instance
		/// </summary>
		private SqlBulkCopy CreateSqlBulkCopy(SqlConnection connection, SqlTransaction transaction)
		{
			SqlBulkCopy copy = new SqlBulkCopy(connection, bulkCopyOptions, transaction);
			foreach (KeyValuePair<string, string> pair in Mappings)
			{
				copy.ColumnMappings.Add(pair.Key, pair.Value);
			}
			copy.DestinationTableName = TargetTable;
			copy.BulkCopyTimeout = Timeout;
			return copy;
		}
	}
}