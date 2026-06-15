using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Philips.HIF.Contracts;

namespace PhilipsHifBridge
{
    internal static class PiChangeFactory
    {
        public static PIChange Create(string adtXmlOrHl7)
        {
            var now = DateTime.Now;
            var parsed = ParseHl7(adtXmlOrHl7);
            var source = CreateSource();
            var after = CreatePatientIdentity(parsed, source, now);

            var descriptor = new PIChangeDescriptor
            {
                ChangedAttributes = new List<PtAttribute>(after.Attributes),
                ChangedLocations = new List<PtLocation>(after.Locations),
                ChangedNumbers = new List<PtNumber>(after.Numbers),
                HL7Msg = adtXmlOrHl7 ?? ""
            };

            return new PIChange
            {
                Id = Guid.NewGuid(),
                IsTest = false,
                Source = new PIClient
                {
                    Domain = PIServiceDomains.HIF,
                    Name = "HL7Gateway.PhilipsHifBridge"
                },
                TriggerTime = now,
                ChangeTrigger = InferTrigger(adtXmlOrHl7),
                Descriptor = descriptor,
                After = after,
                Before = null
            };
        }

        public static PatientIdentity CreatePatientIdentity(PersistedPatient persisted)
        {
            var now = persisted == null || persisted.UpdatedAt == default(DateTime)
                ? DateTime.Now
                : persisted.UpdatedAt;
            var source = CreateSource();
            var parsed = new ParsedAdt
            {
                Mrn = persisted == null ? "" : persisted.Mrn,
                VisitNumber = persisted == null ? "" : persisted.VisitNumber,
                LastName = persisted == null ? "" : persisted.LastName,
                FirstName = persisted == null ? "" : persisted.FirstName,
                MiddleName = persisted == null ? "" : persisted.MiddleName,
                DateOfBirth = persisted == null ? null : persisted.DateOfBirth,
                Gender = persisted == null ? "" : persisted.Gender,
                CareUnit = persisted == null ? "" : persisted.CareUnit,
                Room = persisted == null ? "" : persisted.Room,
                Bed = persisted == null ? "" : persisted.Bed,
                Facility = persisted == null ? "" : persisted.Facility
            };
            return CreatePatientIdentity(parsed, source, now);
        }

        public static PersistedPatient ToPersistedPatient(PatientIdentity patient)
        {
            if (patient == null)
                return null;

            return new PersistedPatient
            {
                Mrn = FindNumber(patient, PtNumberFieldNames.MedicalRecordNumber.ToString()),
                VisitNumber = FindNumber(patient, PtNumberFieldNames.VisitNumber.ToString()),
                LastName = FindAttribute(patient, PtAttributeFieldNames.LastName.ToString()),
                FirstName = FindAttribute(patient, PtAttributeFieldNames.FirstName.ToString()),
                MiddleName = FindAttribute(patient, PtAttributeFieldNames.MiddleName.ToString()),
                DateOfBirth = FindDateTimeAttribute(patient, PtAttributeFieldNames.DOB.ToString()),
                Gender = FindAttribute(patient, PtAttributeFieldNames.Gender.ToString()),
                CareUnit = FindLocation(patient, PtLocationFieldNames.CareUnit.ToString()),
                Room = FindLocation(patient, PtLocationFieldNames.Room.ToString()),
                Bed = FindLocation(patient, PtLocationFieldNames.Bed.ToString()),
                Facility = FindLocation(patient, PtLocationFieldNames.Facility.ToString()),
                UpdatedAt = DateTime.Now
            };
        }

        private static PatientIdentity CreatePatientIdentity(ParsedAdt parsed, PIClientSubscription source, DateTime now)
        {
            var patientId = CreateStablePatientGuid(parsed.Mrn);
            var transactionSource = new PIClient
            {
                Domain = PIServiceDomains.HIF,
                Name = "HL7Gateway.PhilipsHifBridge"
            };
            var attributes = new List<PtAttribute>();
            AddAttribute(attributes, PtAttributeFieldNames.LastName.ToString(), parsed.LastName, source, now);
            AddAttribute(attributes, PtAttributeFieldNames.FirstName.ToString(), parsed.FirstName, source, now);
            AddAttribute(attributes, PtAttributeFieldNames.MiddleName.ToString(), parsed.MiddleName, source, now);
            AddAttribute(attributes, PtAttributeFieldNames.Gender.ToString(), parsed.Gender, source, now);
            if (parsed.DateOfBirth.HasValue)
                attributes.Add(CreateDateTimeAttribute(PtAttributeFieldNames.DOB.ToString(), parsed.DateOfBirth.Value, source, now));

            var numbers = new List<PtNumber>();
            AddNumber(numbers, PtNumberFieldNames.MedicalRecordNumber.ToString(), parsed.Mrn, source, now);
            AddNumber(numbers, PtNumberFieldNames.VisitNumber.ToString(), parsed.VisitNumber, source, now);

            var locations = new List<PtLocation>();
            AddLocation(locations, PtLocationFieldNames.CareUnit.ToString(), parsed.CareUnit, source, now, false);
            AddLocation(locations, PtLocationFieldNames.Room.ToString(), parsed.Room, source, now, false);
            AddLocation(locations, PtLocationFieldNames.Bed.ToString(), parsed.Bed, source, now, true);
            AddLocation(locations, PtLocationFieldNames.Facility.ToString(), parsed.Facility, source, now, false);

            return new PatientIdentity
            {
                Id = patientId,
                UniqueId = patientId,
                ChangeTriggers = InferTrigger(parsed.MessageType),
                TransactionSource = transactionSource,
                Attributes = attributes,
                Numbers = numbers,
                Locations = locations
            };
        }

        private static PIClientSubscription CreateSource()
        {
            return new PIClientSubscription
            {
                Domain = PIServiceDomains.HIF,
                Name = "HL7Gateway.PhilipsHifBridge",
                LocationOfInterest = new List<string>(),
                SubscriptionMode = PISubscriptionModes.Both
            };
        }

        private static void AddAttribute(List<PtAttribute> attributes, string fieldName, string value, PIClientSubscription source, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            attributes.Add(new PtAttributeString
            {
                FieldName = fieldName,
                Value = value.Trim(),
                Source = source,
                StartTime = now,
                StoreTime = now,
                EndTime = DateTime.MaxValue,
                IsAuthoritative = true
            });
        }

        private static PtAttributeDateTime CreateDateTimeAttribute(string fieldName, DateTime value, PIClientSubscription source, DateTime now)
        {
            return new PtAttributeDateTime
            {
                FieldName = fieldName,
                Value = value,
                Source = source,
                StartTime = now,
                StoreTime = now,
                EndTime = DateTime.MaxValue,
                IsAuthoritative = true
            };
        }

        private static void AddNumber(List<PtNumber> numbers, string fieldName, string value, PIClientSubscription source, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            numbers.Add(new PtNumber
            {
                FieldName = fieldName,
                Value = value.Trim(),
                Source = source,
                StartTime = now,
                StoreTime = now,
                EndTime = DateTime.MaxValue,
                IsAuthoritative = true
            });
        }

        private static void AddLocation(List<PtLocation> locations, string fieldName, string value, PIClientSubscription source, DateTime now, bool singleOccupancy)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            locations.Add(new PtLocation
            {
                FieldName = fieldName,
                Value = value.Trim(),
                IsSingleOccupancy = singleOccupancy,
                Source = source,
                StartTime = now,
                StoreTime = now,
                EndTime = DateTime.MaxValue,
                IsAuthoritative = true
            });
        }

        private static ParsedAdt ParseHl7(string text)
        {
            var parsed = new ParsedAdt();
            text = text ?? "";
            var normalized = text.Replace("\r\n", "\r").Replace("\n", "\r");
            foreach (var segment in normalized.Split(new[] { '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = segment.Split('|');
                if (fields.Length == 0)
                    continue;

                if (fields[0] == "MSH")
                {
                    parsed.MessageType = GetField(fields, 8);
                }
                else if (fields[0] == "PID")
                {
                    ParsePatientNumbers(GetField(fields, 3), parsed);
                    ParseName(GetField(fields, 5), parsed);
                    parsed.DateOfBirth = ParseDate(GetField(fields, 7));
                    parsed.Gender = FirstComponent(GetField(fields, 8));
                }
                else if (fields[0] == "PV1")
                {
                    var location = GetField(fields, 3).Split('^');
                    parsed.CareUnit = GetComponent(location, 0);
                    parsed.Room = GetComponent(location, 1);
                    parsed.Bed = GetComponent(location, 2);
                    parsed.Facility = GetComponent(location, 3);
                }
            }

            return parsed;
        }

        private static void ParsePatientNumbers(string value, ParsedAdt parsed)
        {
            foreach (var repeat in (value ?? "").Split('~'))
            {
                var components = repeat.Split('^');
                var number = GetComponent(components, 0);
                var type = GetComponent(components, 4);
                if (string.Equals(type, "VN", StringComparison.OrdinalIgnoreCase))
                    parsed.VisitNumber = number;
                else if (string.Equals(type, "MR", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(parsed.Mrn))
                    parsed.Mrn = number;
            }
        }

        private static void ParseName(string value, ParsedAdt parsed)
        {
            var name = FirstRepeat(value).Split('^');
            parsed.LastName = CleanName(GetComponent(name, 0));
            parsed.FirstName = CleanName(GetComponent(name, 1));
            parsed.MiddleName = CleanName(GetComponent(name, 2));
        }

        private static DateTime? ParseDate(string value)
        {
            value = FirstComponent(value);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime date;
            foreach (var format in new[] { "yyyyMMdd", "yyyyMMddHHmmss", "yyyyMMddHHmm" })
            {
                if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    return date;
            }

            return null;
        }

        private static string GetField(string[] fields, int index)
        {
            return index >= 0 && index < fields.Length ? fields[index] : "";
        }

        private static string GetComponent(string[] components, int index)
        {
            return index >= 0 && index < components.Length ? components[index] : "";
        }

        private static string FirstRepeat(string value)
        {
            return (value ?? "").Split('~')[0];
        }

        private static string FirstComponent(string value)
        {
            return (value ?? "").Split('^')[0];
        }

        private static string CleanName(string value)
        {
            value = (value ?? "").Trim();
            return value == "\"\"" ? "" : value;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        }

        private static Guid CreateStablePatientGuid(string mrn)
        {
            if (string.IsNullOrWhiteSpace(mrn))
                return Guid.NewGuid();

            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("HL7Gateway.PhilipsHifBridge:" + mrn.Trim()));
                return new Guid(bytes);
            }
        }

        private static string FindNumber(PatientIdentity patient, string fieldName)
        {
            if (patient == null || patient.Numbers == null)
                return "";

            var number = patient.Numbers.FirstOrDefault(n => string.Equals(n.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            return number == null ? "" : number.Value;
        }

        private static string FindAttribute(PatientIdentity patient, string fieldName)
        {
            if (patient == null || patient.Attributes == null)
                return "";

            var attribute = patient.Attributes.FirstOrDefault(a => string.Equals(a.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? "" : attribute.StringValue;
        }

        private static DateTime? FindDateTimeAttribute(PatientIdentity patient, string fieldName)
        {
            if (patient == null || patient.Attributes == null)
                return null;

            var attribute = patient.Attributes.OfType<PtAttributeDateTime>()
                .FirstOrDefault(a => string.Equals(a.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? (DateTime?)null : attribute.Value;
        }

        private static string FindLocation(PatientIdentity patient, string fieldName)
        {
            if (patient == null || patient.Locations == null)
                return "";

            var location = patient.Locations.FirstOrDefault(l => string.Equals(l.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            return location == null ? "" : location.Value;
        }

        private static PIChangeTriggers InferTrigger(string text)
        {
            text = text ?? "";
            if (Contains(text, "ADT_A03") || Contains(text, "ADT^A03"))
                return PIChangeTriggers.Discharge;
            if (Contains(text, "ADT_A02") || Contains(text, "ADT^A02"))
                return PIChangeTriggers.Transfer;
            if (Contains(text, "ADT_A08") || Contains(text, "ADT^A08"))
                return PIChangeTriggers.UpdateInformation;
            if (Contains(text, "ADT_A04") || Contains(text, "ADT^A04"))
                return PIChangeTriggers.Register;
            return PIChangeTriggers.Admit;
        }

        private static bool Contains(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ParsedAdt
        {
            public string MessageType;
            public string Mrn;
            public string VisitNumber;
            public string LastName;
            public string FirstName;
            public string MiddleName;
            public DateTime? DateOfBirth;
            public string Gender;
            public string CareUnit;
            public string Room;
            public string Bed;
            public string Facility;
        }
    }
}
