/*
    Copyright 2020 Jeffrey Sharp

    Permission to use, copy, modify, and distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Data;
using System.Security;
using Microsoft.Data.SqlClient;

namespace PSql
{
    /// <summary>
    ///   Top-level interface between PSql and PSql.Client.
    /// </summary>
    public class PSqlClient
    {
        static PSqlClient()
        {
            SniLoader.Load();
        }

        /// <summary>
        ///   Creates a new <see cref="SqlConnectionStringBuilder"/> instance.
        /// </summary>
        public SqlConnectionStringBuilder CreateConnectionStringBuilder()
            => new SqlConnectionStringBuilder();

        /// <summary>
        ///   Creates and opens a new <see cref="SqlConnection"/> instance
        ///   using the specified connection string, logging server messages
        ///   via the specified delegates.
        /// </summary>
        /// <param name="connectionString">
        ///   Gets the connection string used to create this connection.  The
        ///   connection string includes server name, database name, and other
        ///   parameters that control the initial opening of the connection.
        /// </param>
        /// <param name="writeInformation">
        ///   Delegate that logs server informational messages.
        /// </param>
        /// <param name="writeWarning">
        ///   Delegate that logs server warning or error messages.
        /// </param>
        public SqlConnection Connect(
            string         connectionString,
            Action<string> writeInformation,
            Action<string> writeWarning)
        {
            if (connectionString is null)
                throw new ArgumentNullException(nameof(connectionString));
            if (writeInformation is null)
                throw new ArgumentNullException(nameof(writeInformation));
            if (writeWarning is null)
                throw new ArgumentNullException(nameof(writeWarning));

            return ConnectCore(
                new SqlConnection(connectionString),
                writeInformation,
                writeWarning
            );
        }

        /// <summary>
        ///   Creates and opens a new <see cref="SqlConnection"/> instance
        ///   using the specified connection string and credential, logging
        ///   server messages via the specified delegates.
        /// </summary>
        /// <param name="connectionString">
        ///   Gets the connection string used to create this connection.  The
        ///   connection string includes server name, database name, and other
        ///   parameters that control the initial opening of the connection.
        /// </param>
        /// <param name="username">
        ///   The username to present for authentication.
        /// </param>
        /// <param name="password">
        ///   The password to present for authentication.
        /// </param>
        /// <param name="writeInformation">
        ///   Delegate that logs server informational messages.
        /// </param>
        /// <param name="writeWarning">
        ///   Delegate that logs server warning or error messages.
        /// </param>
        public SqlConnection Connect(
            string         connectionString,
            string         username,
            SecureString   password,
            Action<string> writeInformation,
            Action<string> writeWarning)
        {
            if (connectionString is null)
                throw new ArgumentNullException(nameof(connectionString));
            if (username is null)
                throw new ArgumentNullException(nameof(username));
            if (password is null)
                throw new ArgumentNullException(nameof(password));
            if (writeInformation is null)
                throw new ArgumentNullException(nameof(writeInformation));
            if (writeWarning is null)
                throw new ArgumentNullException(nameof(writeWarning));

            if (!password.IsReadOnly())
                (password = password.Copy()).MakeReadOnly();

            var credential = new SqlCredential(username, password);

            return ConnectCore(
                new SqlConnection(connectionString, credential),
                writeInformation,
                writeWarning
            );
        }

        private SqlConnection ConnectCore(
            SqlConnection  connection,
            Action<string> writeInformation,
            Action<string> writeWarning)
        {
            var info = null as ConnectionInfo;

            try
            {
                info = ConnectionInfo.Get(connection);

                connection.FireInfoMessageEventOnUserErrors = true;
                connection.InfoMessage += (sender, e) =>
                {
                    if (sender is SqlConnection c)
                        HandleMessage(c, e.Errors, writeInformation, writeWarning);
                };

                connection.Open();

                return connection;
            }
            catch
            {
                if (info != null)
                    info.IsDisconnecting = true;

                connection?.Dispose();
                throw;
            }
        }

        private void HandleMessage(
            SqlConnection      connection,
            SqlErrorCollection errors,
            Action<string>     writeInformation,
            Action<string>     writeWarning)
        {
            const int MaxInformationalSeverity = 10;

            foreach (SqlError? error in errors)
            {
                if (error is null)
                {
                    // Do nothing
                }
                else if (error.Class <= MaxInformationalSeverity)
                {
                    // Output as normal text
                    writeInformation(error.Message);
                }
                else
                {
                    // Output as warning
                    writeWarning(Format(error));

                    // Mark current command as failed
                    ConnectionInfo.Get(connection).HasErrors = true;
                }
            }
        }

        private static string Format(SqlError error)
        {
            const string NonProcedureLocationName = "(batch)";

            var procedure
                =  error.Procedure.NullIfEmpty()
                ?? NonProcedureLocationName;

            return $"{procedure}:{error.LineNumber}: E{error.Class}: {error.Message}";
        }

        /// <summary>
        ///   Returns a value indicating whether errors have been logged on the
        ///   specified connection.
        /// </summary>
        /// <param name="connection">
        ///   The connection to check.
        /// </param>
        public bool HasErrors(SqlConnection connection)
        {
            return ConnectionInfo.Get(connection).HasErrors;
        }

        /// <summary>
        ///   Clears any errors prevously logged on the specified connection.
        /// </summary>
        /// <param name="connection">
        ///   The connection for which to clear error state.
        /// </param>
        public void ClearErrors(SqlConnection connection)
        {
            ConnectionInfo.Get(connection).HasErrors = false;
        }

        /// <summary>
        ///   Indicates that the specified connection is expected to
        ///   disconnect.
        /// </summary>
        /// <param name="connection">
        ///   The connection that is expected to disconnect.
        /// </param>
        public void SetDisconnecting(SqlConnection connection)
        {
            ConnectionInfo.Get(connection).IsDisconnecting = true;
        }

        /// <summary>
        ///   Executes the specified <see cref="SqlCommand"/> and projects
        ///   results to objects using the specified delegates.
        /// </summary>
        /// <param name="command">
        ///   The command to execute.
        /// </param>
        /// <param name="createObject">
        ///   Delegate that creates a result object.
        /// </param>
        /// <param name="setProperty">
        ///   Delegate that sets a property on a result object.
        /// </param>
        /// <param name="useSqlTypes">
        ///   <c>false</c> to project fields using CLR types from the
        ///     <see cref="System"/> namespace, such as <see cref="int"/>.
        ///   <c>true</c> to project fields using SQL types from the
        ///     <see cref="System.Data.SqlTypes"/> namespace, such as
        ///     <see cref="System.Data.SqlTypes.SqlInt32"/>.
        /// </param>
        /// <returns>
        ///   A sequence of objects created by executing
        ///     <paramref name="command"/>
        ///   and projecting each result row to an object using
        ///     <paramref name="createObject"/>,
        ///     <paramref name="setProperty"/>, and
        ///     <paramref name="useSqlTypes"/>,
        ///   in the order produced by the command.  If the command produces no
        ///   result rows, this method returns an empty sequence.
        /// </returns>
        public IEnumerator<object> ExecuteAndProject(
            SqlCommand                      command,
            Func<object>                    createObject,
            Action<object, string, object?> setProperty,
            bool                            useSqlTypes = false)
        {
            if (command is null)
                throw new ArgumentNullException(nameof(command));

            if (command.Connection.State == ConnectionState.Closed)
                command.Connection.Open();

            var reader = command.ExecuteReader();
            // dispose if error

            return new ObjectResultSet(reader, createObject, setProperty, useSqlTypes);
        }
    }
}
