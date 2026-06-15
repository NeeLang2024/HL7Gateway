using System;

namespace PhilipsHifBridge
{
    internal sealed class PersistedPatient
    {
        public string Mrn { get; set; }
        public string VisitNumber { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string CareUnit { get; set; }
        public string Room { get; set; }
        public string Bed { get; set; }
        public string Facility { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
