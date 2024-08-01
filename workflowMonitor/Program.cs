namespace workflowMonitor
{
    using System;
    using Microsoft.Data.SqlClient;
    using System.Threading.Tasks;

    class Program
    {
        public static Utilities_NetCore.clsConfig myConfig = new Utilities_NetCore.clsConfig();

        static async Task Main(string[] args)
        {
            // set reference variables
            string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
            string configFile = Path.Combine(projectDir, "appsettings.json");
            myConfig.configFile = configFile;

#if DEBUG
            string connectionString = myConfig.getConfig("connectionStringDev");
#else
            string connectionString = myConfig.getConfig("connectionStringProd");
#endif
            var connection = new SqlConnection(connectionString);

            TimeSpan startTime = new TimeSpan(8, 0, 0);
            TimeSpan endTime = new TimeSpan(20, 0, 0);

            Boolean executeEvents = canExecuteEvents(startTime, endTime);
            while (executeEvents)
            {
                // check if database connection is open, if not, open it
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                // query database for events that need to be started (proc dbo.pendingEvents)
                var pendingEvents = new List<clsEvent>();

                var command = new SqlCommand();
                command.Connection = connection;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = "dbo.pendingEvents";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var eventRecord = new clsEvent();

                        eventRecord.eventID = reader["eventID"] != DBNull.Value ? Convert.ToInt32(reader["eventID"]) : default;
                        eventRecord.actionID = reader["actionID"] != DBNull.Value ? Convert.ToInt32(reader["actionID"]) : default;
                        eventRecord.applicationFilename = reader["applicationFilename"] != DBNull.Value ? reader["applicationFilename"] as string : null;
                        eventRecord.eventParameters = reader["eventParameters"] != DBNull.Value ? reader["eventParameters"] as string : null;

                        pendingEvents.Add(eventRecord);
                    }
                }
                command.Dispose();

                // iterate through event records
                command.Connection = connection;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = "dbo.canStartEvent";
                foreach (clsEvent evt in pendingEvents)
                {
                    // query if an event can be started (proc dbo.canStartEvent)
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@eventID", evt.eventID);
                    var result = command.ExecuteScalar();
                    if (result != DBNull.Value && Convert.ToInt16(result) == 1)
                    {
                        // TODO: event can be started, start it asyncronously
                    }
                }
                command.Dispose();

                // TODO: when event ends/errors, need to update DB

                // sleep for the defined period then re-populate executeEvents to exit loop when necessary
                int sleepSeconds = 60;
                Thread.Sleep(1000 * sleepSeconds);
                executeEvents = canExecuteEvents(startTime, endTime);

                // TODO: confirm if I'm processing async I'm able to exit this loop while events are still running
            }

            // clean-up
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
            }

        }

        static Boolean canExecuteEvents(TimeSpan startTime, TimeSpan endTime)
        {
            TimeSpan currentTime = DateTime.Now.TimeOfDay;
            if (currentTime >= startTime && currentTime <= endTime)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }

}
