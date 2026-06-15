using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace PhilipsHifBridge
{
    internal sealed class PatientStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private readonly BridgeLogger _logger;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private string _connectionString;
        private string _storageMode = "Json";

        public PatientStore(BridgeLogger logger)
        {
            _logger = logger;
            _path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patients.json");
            _serializer.MaxJsonLength = int.MaxValue;
        }

        public void Configure(string connectionString)
        {
            _connectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
            _storageMode = string.IsNullOrWhiteSpace(_connectionString) ? "Json" : "SqlServer";
            _logger.Info("[STORE] Patient store configured: " + SafeStoreDescription);
        }

        public string Path
        {
            get { return _path; }
        }

        public string StorageMode
        {
            get { return _storageMode; }
        }

        public string SafeStoreDescription
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_connectionString))
                    return "SQL Server: " + RedactConnectionString(_connectionString);
                return _path;
            }
        }

        public List<PersistedPatient> Load()
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_connectionString))
                {
                    var sqlPatients = TryLoadFromSql();
                    if (sqlPatients != null)
                        return sqlPatients;

                    _logger.Warning("[STORE] Falling back to local JSON patient cache after SQL load failure");
                }

                if (!File.Exists(_path))
                {
                    _logger.Info("[STORE] Patient store not found; starting with empty cache");
                    return new List<PersistedPatient>();
                }

                try
                {
                    var json = File.ReadAllText(_path);
                    var patients = _serializer.Deserialize<List<PersistedPatient>>(json) ?? new List<PersistedPatient>();
                    _logger.Info(string.Format("[STORE] Loaded {0} patient(s) from {1}", patients.Count, _path));
                    return patients;
                }
                catch (Exception ex)
                {
                    _logger.Warning(string.Format("[STORE] Failed to load patient store {0}: {1}", _path, ex.Message));
                    return new List<PersistedPatient>();
                }
            }
        }

        public void Save(IEnumerable<PersistedPatient> patients)
        {
            lock (_sync)
            {
                var list = (patients ?? Enumerable.Empty<PersistedPatient>())
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Mrn))
                    .ToList();

                if (!string.IsNullOrWhiteSpace(_connectionString) && TrySaveToSql(list))
                    return;

                if (!string.IsNullOrWhiteSpace(_connectionString))
                    _logger.Warning("[STORE] Falling back to local JSON patient cache after SQL save failure");

                try
                {
                    var dir = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    var json = _serializer.Serialize(list);
                    File.WriteAllText(_path, json);
                }
                catch (Exception ex)
                {
                    _logger.Warning(string.Format("[STORE] Failed to save patient store {0}: {1}", _path, ex.Message));
                }
            }
        }

        private List<PersistedPatient> TryLoadFromSql()
        {
            try
            {
                var patients = new List<PersistedPatient>();
                using (var connection = new SqlConnection(_connectionString))
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT
    p.PatientId,
    p.Name,
    p.DateOfBirth,
    p.Gender,
    v.VisitId,
    v.Department,
    v.Ward,
    v.Room,
    v.Bed,
    COALESCE(v.UpdatedAt, p.UpdatedAt, p.CreatedAt) AS UpdatedAt
FROM dbo.Patients p
OUTER APPLY (
    SELECT TOP 1 *
    FROM dbo.Visits v
    WHERE v.PatientId = p.PatientId
    ORDER BY
        CASE WHEN v.DischargeDateTime IS NULL THEN 0 ELSE 1 END,
        v.AdmitDateTime DESC,
        v.UpdatedAt DESC
) v
ORDER BY p.UpdatedAt DESC";
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var name = ReadString(reader, "Name");
                            SplitName(name, out var lastName, out var firstName);
                            patients.Add(new PersistedPatient
                            {
                                Mrn = ReadString(reader, "PatientId"),
                                VisitNumber = ReadString(reader, "VisitId"),
                                LastName = lastName,
                                FirstName = firstName,
                                DateOfBirth = ReadNullableDateTime(reader, "DateOfBirth"),
                                Gender = ReadString(reader, "Gender"),
                                CareUnit = ReadString(reader, "Department"),
                                Facility = ReadString(reader, "Ward"),
                                Room = ReadString(reader, "Room"),
                                Bed = ReadString(reader, "Bed"),
                                UpdatedAt = ReadNullableDateTime(reader, "UpdatedAt") ?? DateTime.Now
                            });
                        }
                    }
                }

                _storageMode = "SqlServer";
                _logger.Info(string.Format("[STORE] Loaded {0} patient(s) from SQL Server ({1})", patients.Count, RedactConnectionString(_connectionString)));
                return patients;
            }
            catch (Exception ex)
            {
                _logger.Warning(string.Format("[STORE] Failed to load patients from SQL Server: {0}", ex.Message));
                return null;
            }
        }

        private bool TrySaveToSql(List<PersistedPatient> patients)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    foreach (var patient in patients)
                    {
                        UpsertPatient(connection, patient);
                        UpsertVisit(connection, patient);
                    }
                }

                _storageMode = "SqlServer";
                _logger.Info(string.Format("[STORE] Saved {0} patient(s) to SQL Server", patients.Count));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(string.Format("[STORE] Failed to save patients to SQL Server: {0}", ex.Message));
                return false;
            }
        }

        private static void UpsertPatient(SqlConnection connection, PersistedPatient patient)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Patients WHERE PatientId = @PatientId)
BEGIN
    UPDATE dbo.Patients
    SET
        PatientIdList = COALESCE(NULLIF(@PatientIdList, ''), PatientIdList),
        Name = COALESCE(NULLIF(@Name, ''), Name),
        DateOfBirth = COALESCE(@DateOfBirth, DateOfBirth),
        Gender = COALESCE(NULLIF(@Gender, ''), Gender),
        UpdatedAt = @Now
    WHERE PatientId = @PatientId
END
ELSE
BEGIN
    INSERT INTO dbo.Patients
        (PatientId, PatientIdList, Name, DateOfBirth, Gender, CreatedAt, UpdatedAt)
    VALUES
        (@PatientId, @PatientIdList, @Name, @DateOfBirth, @Gender, @Now, @Now)
END";
                AddCommonPatientParameters(command, patient);
                command.ExecuteNonQuery();
            }
        }

        private static void UpsertVisit(SqlConnection connection, PersistedPatient patient)
        {
            var visitId = string.IsNullOrWhiteSpace(patient.VisitNumber)
                ? patient.Mrn + "_V1"
                : patient.VisitNumber;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Visits WHERE VisitId = @VisitId)
BEGIN
    UPDATE dbo.Visits
    SET
        PatientId = @PatientId,
        PatientClass = COALESCE(NULLIF(@PatientClass, ''), PatientClass),
        Department = COALESCE(NULLIF(@Department, ''), Department),
        Ward = COALESCE(NULLIF(@Ward, ''), Ward),
        Room = COALESCE(NULLIF(@Room, ''), Room),
        Bed = COALESCE(NULLIF(@Bed, ''), Bed),
        AdmitDateTime = COALESCE(AdmitDateTime, @Now),
        UpdatedAt = @Now
    WHERE VisitId = @VisitId
END
ELSE
BEGIN
    INSERT INTO dbo.Visits
        (VisitId, PatientId, AdmitDateTime, PatientClass, Department, Ward, Room, Bed, CreatedAt, UpdatedAt)
    VALUES
        (@VisitId, @PatientId, @Now, @PatientClass, @Department, @Ward, @Room, @Bed, @Now, @Now)
END";
                command.Parameters.Add("@VisitId", SqlDbType.NVarChar, 100).Value = visitId;
                command.Parameters.Add("@PatientId", SqlDbType.NVarChar, 100).Value = patient.Mrn;
                command.Parameters.Add("@PatientClass", SqlDbType.NVarChar, 50).Value = "I";
                command.Parameters.Add("@Department", SqlDbType.NVarChar, 100).Value = DbValue(patient.CareUnit);
                command.Parameters.Add("@Ward", SqlDbType.NVarChar, 100).Value = DbValue(patient.Facility);
                command.Parameters.Add("@Room", SqlDbType.NVarChar, 50).Value = DbValue(patient.Room);
                command.Parameters.Add("@Bed", SqlDbType.NVarChar, 50).Value = DbValue(patient.Bed);
                command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = DateTime.Now;
                command.ExecuteNonQuery();
            }
        }

        private static void AddCommonPatientParameters(SqlCommand command, PersistedPatient patient)
        {
            command.Parameters.Add("@PatientId", SqlDbType.NVarChar, 100).Value = patient.Mrn;
            command.Parameters.Add("@PatientIdList", SqlDbType.NVarChar, 500).Value = patient.Mrn + "^^^^MR";
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = DbValue(JoinName(patient.LastName, patient.FirstName));
            command.Parameters.Add("@DateOfBirth", SqlDbType.Date).Value = patient.DateOfBirth.HasValue ? (object)patient.DateOfBirth.Value.Date : DBNull.Value;
            command.Parameters.Add("@Gender", SqlDbType.NVarChar, 1).Value = DbValue(patient.Gender);
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = DateTime.Now;
        }

        private static object DbValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();
        }

        private static string ReadString(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";
        }

        private static DateTime? ReadNullableDateTime(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static string JoinName(string lastName, string firstName)
        {
            if (string.IsNullOrWhiteSpace(lastName)) return firstName ?? "";
            if (string.IsNullOrWhiteSpace(firstName)) return lastName ?? "";
            return lastName + firstName;
        }

        private static void SplitName(string name, out string lastName, out string firstName)
        {
            name = name ?? "";
            lastName = name;
            firstName = "";
        }

        private static string RedactConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                var server = Convert.ToString(builder["Data Source"]);
                var database = Convert.ToString(builder["Initial Catalog"]);
                return server + "/" + database;
            }
            catch
            {
                return "(configured)";
            }
        }
    }
}
