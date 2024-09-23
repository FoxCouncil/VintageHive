using Microsoft.Data.Sqlite;
using SharpIpp.Protocol.Models;

namespace VintageHive.Data.Contexts;

public class PrinterDbContext : DbContextBase
{
    private const string TABLE_JOBS = "jobs";

    public PrinterDbContext() : base()
    {
        CreateTable(TABLE_JOBS, "id INTEGER PRIMARY KEY AUTOINCREMENT, state INTEGER, name TEXT, docAttr TEXT, docNewAttr TEXT, docData BLOB, printData BLOB, printType TEXT, created DATETIME, processed DATETIME, completed DATETIME");
    }

    public int CreateJob(string name)
    {
        var id = 0;

        WithContext(context =>
        {
            using var insertCommand = context.CreateCommand();

            insertCommand.CommandText = $"INSERT INTO {TABLE_JOBS} (state, name, created) VALUES(@state, @name, @created); SELECT last_insert_rowid();";

            insertCommand.Parameters.Add(new SqliteParameter("@state", value: PrinterJobState.New));
            insertCommand.Parameters.Add(new SqliteParameter("@name", name));
            insertCommand.Parameters.Add(new SqliteParameter("@created", DateTime.UtcNow));

            id = Convert.ToInt32(insertCommand.ExecuteScalar());
        });

        return id;
    }

    public PrinterState GetPrinterState()
    {
        PrinterState printerState = PrinterState.Idle;

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT COUNT(1) FROM {TABLE_JOBS} WHERE state = @pending OR state = @processing";

            selectCommand.Parameters.Add(new SqliteParameter("@pending", (int)PrinterJobState.Pending));
            selectCommand.Parameters.Add(new SqliteParameter("@processing", (int)PrinterJobState.Processing));

            var result = selectCommand.ExecuteScalar();

            if (Convert.ToInt32(result) > 0)
            {
                printerState = PrinterState.Processing;
            }
        });

        return printerState;
    }

    public PrinterJob GetNextJob()
    {
        PrinterJob job = null;

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT id, state, name, docAttr, docNewAttr, docData, created, processed, completed FROM {TABLE_JOBS} WHERE state = @pending ORDER BY created ASC LIMIT 1";

            selectCommand.Parameters.Add(new SqliteParameter("@pending", (int)PrinterJobState.Pending));

            using var reader = selectCommand.ExecuteReader();

            if (reader.Read())
            {
                job = new PrinterJob
                {
                    Id = reader.GetInt32(0),
                    State = (PrinterJobState)reader.GetInt32(1),
                    Name = reader.GetString(2),
                    DocAttr = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DocNewAttr = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    DocData = reader.IsDBNull(5) ? [] : (byte[])reader.GetValue(5),
                    Created = reader.GetDateTime(6),
                    Processed = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                    Completed = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8)
                };
            }
        });

        return job;
    }

    public List<PrinterJob> GetAllJobs()
    {
        List<PrinterJob> jobs = [];

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT id, state, name, created, processed, completed FROM {TABLE_JOBS} ORDER BY created DESC LIMIT 300";

            using var reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                var job = new PrinterJob
                {
                    Id = reader.GetInt32(0),
                    State = (PrinterJobState)reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Created = reader.GetDateTime(3),
                    Processed = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                    Completed = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5)
                };

                jobs.Add(job);
            }
        });

        return jobs;
    }

    public int GetProcessingJobCount()
    {
        int jobCount = 0;

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT COUNT(*) FROM {TABLE_JOBS} WHERE state = @pending OR state = @processing";
            selectCommand.Parameters.Add(new SqliteParameter("@pending", (int)PrinterJobState.Pending));
            selectCommand.Parameters.Add(new SqliteParameter("@processing", (int)PrinterJobState.Processing));

            var result = selectCommand.ExecuteScalar();

            jobCount = Convert.ToInt32(result);
        });

        return jobCount;
    }

    public PrinterJob GetJob(int id)
    {
        PrinterJob job = null;

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT id, state, name, created, processed, completed FROM {TABLE_JOBS} WHERE id = @id";
            selectCommand.Parameters.Add(new SqliteParameter("@id", id));

            using var reader = selectCommand.ExecuteReader();

            if (reader.Read())
            {
                job = new PrinterJob
                {
                    Id = reader.GetInt32(0),
                    State = (PrinterJobState)reader.GetInt32(1),  // Assuming PrinterJobState is an enum
                    Name = reader.GetString(2),
                    Created = reader.GetDateTime(3),
                    Processed = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    Completed = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                };
            }
        });

        return job;
    }

    public PrinterJobState? GetJobState(int id)
    {
        PrinterJobState? state = null;

        WithContext(context =>
        {
            using var selectCommand = context.CreateCommand();

            selectCommand.CommandText = $"SELECT state FROM {TABLE_JOBS} WHERE id = @id";

            selectCommand.Parameters.Add(new SqliteParameter("@id", id));

            using var reader = selectCommand.ExecuteReader();

            if (reader.Read())
            {
                state = (PrinterJobState)reader.GetInt32(0);
            }
        });

        return state;
    }

    public bool SetJobDocumentData(int id, string docAttr, string docNewAttr, byte[] docData)
    {
        bool success = false;

        WithContext(context =>
        {
            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_JOBS} SET state = @state, docAttr = @docAttr, docNewAttr = @docNewAttr, docData = @docData WHERE id = @id";

            updateCommand.Parameters.Add(new SqliteParameter("@state", PrinterJobState.Pending));
            updateCommand.Parameters.Add(new SqliteParameter("@docAttr", docAttr));
            updateCommand.Parameters.Add(new SqliteParameter("@docNewAttr", docNewAttr));
            updateCommand.Parameters.Add(new SqliteParameter("@docData", docData));
            updateCommand.Parameters.Add(new SqliteParameter("@id", id));

            var rowsAffected = updateCommand.ExecuteNonQuery();

            success = rowsAffected > 0;
        });

        if (success)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, nameof(PrinterDbContext), $"SetJobDocumentData: Updated job {id} with document data");
        }

        return success;
    }

    public bool SetJobPrintData(int id, byte[] printData, string printType)
    {
        bool success = false;

        WithContext(context =>
        {
            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_JOBS} SET printData = @printData, printType = @printType WHERE id = @id";

            updateCommand.Parameters.Add(new SqliteParameter("@printData", printData));
            updateCommand.Parameters.Add(new SqliteParameter("@printType", printType));
            updateCommand.Parameters.Add(new SqliteParameter("@id", id));

            var rowsAffected = updateCommand.ExecuteNonQuery();

            success = rowsAffected > 0;
        });

        return success;
    }

    public bool SetJobState(int id, PrinterJobState newState)
    {
        bool success = false;

        WithContext(context =>
        {
            // Begin transaction
            using var transaction = context.BeginTransaction();

            try
            {
                // Retrieve current state of the job
                using var selectCommand = context.CreateCommand();

                selectCommand.CommandText = $"SELECT state, processed, completed FROM {TABLE_JOBS} WHERE id = @id";

                selectCommand.Parameters.Add(new SqliteParameter("@id", id));

                using var reader = selectCommand.ExecuteReader();

                if (!reader.Read())
                {
                    // Job not found
                    success = false;

                    transaction.Rollback();

                    return;
                }

                var currentState = (PrinterJobState)reader.GetInt32(0);
                var processed = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                var completed = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);

                // Determine if the state change is allowed and what fields to update
                var now = DateTime.UtcNow;
                var allowStateChange = false;
                var newProcessed = processed;
                var newCompleted = completed;
                var needToClearDocumentStreams = false;

                switch (newState)
                {
                    case PrinterJobState.Pending when currentState == PrinterJobState.Aborted:
                    {
                        allowStateChange = true;
                    }
                    break;

                    case PrinterJobState.Processing when currentState == PrinterJobState.Pending:
                    {
                        allowStateChange = true;
                        newProcessed = now;
                    }
                    break;

                    case PrinterJobState.Canceled when currentState == PrinterJobState.Pending:
                    {
                        allowStateChange = true;

                        newProcessed = now;
                        newCompleted = now;

                        needToClearDocumentStreams = true;
                    }
                    break;

                    case PrinterJobState.Completed when currentState == PrinterJobState.Processing:
                    case PrinterJobState.Completed when currentState == PrinterJobState.Pending:
                    {
                        allowStateChange = true;
                        newCompleted = now;
                        needToClearDocumentStreams = true;
                    }
                    break;

                    case PrinterJobState.Aborted when currentState == PrinterJobState.Processing:
                    {
                        allowStateChange = true;
                        newCompleted = now;
                    }
                    break;

                    default:
                    {
                        // State change not allowed
                        allowStateChange = false;
                    }
                    break;
                }

                if (allowStateChange)
                {
                    // Update the job in the database
                    using var updateCommand = context.CreateCommand();

                    updateCommand.CommandText = $"UPDATE {TABLE_JOBS} SET state = @state, processed = @processed, completed = @completed WHERE id = @id";

                    updateCommand.Parameters.Add(new SqliteParameter("@state", (int)newState));
                    updateCommand.Parameters.Add(new SqliteParameter("@processed", newProcessed.HasValue ? (object)newProcessed.Value : DBNull.Value));
                    updateCommand.Parameters.Add(new SqliteParameter("@completed", newCompleted.HasValue ? (object)newCompleted.Value : DBNull.Value));
                    updateCommand.Parameters.Add(new SqliteParameter("@id", id));

                    var rowsAffected = updateCommand.ExecuteNonQuery();

                    success = rowsAffected > 0;

                    if (success)
                    {
                        // Commit transaction
                        transaction.Commit();

                        // Optionally clear document streams if needed
                        if (needToClearDocumentStreams)
                        {
                            // this will be done later...
                        }
                    }
                    else
                    {
                        // Update failed
                        transaction.Rollback();
                    }
                }
                else
                {
                    // State change not allowed
                    success = false;
                    transaction.Rollback();
                }
            }
            catch
            {
                // Rollback transaction on error
                transaction.Rollback();
                throw;
            }
        });

        return success;
    }
}
