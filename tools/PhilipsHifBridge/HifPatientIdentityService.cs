using System;
using Philips.HIF.Contracts;

namespace PhilipsHifBridge
{
    internal sealed class HifPatientIdentityService : IPatientIdentity
    {
        public Guid Execute(PIChange change)
        {
            Console.WriteLine("[HIF] IPatientIdentity.Execute called: id={0}, trigger={1}, hl7Length={2}",
                change == null ? Guid.Empty : change.Id,
                change == null ? "(null)" : change.ChangeTrigger.ToString(),
                change == null || change.Descriptor == null || change.Descriptor.HL7Msg == null ? 0 : change.Descriptor.HL7Msg.Length);

            return change == null || change.Id == Guid.Empty ? Guid.NewGuid() : change.Id;
        }
    }
}
