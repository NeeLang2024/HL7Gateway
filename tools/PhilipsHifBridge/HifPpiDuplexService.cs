using System;
using System.Collections.Generic;
using Philips.HIF.Contracts;

namespace PhilipsHifBridge
{
    internal sealed class HifPpiDuplexService : IPIDuplexService, IPatientIdentity
    {
        private static HifBridgeState _state;
        private static BridgeLogger _logger;

        public static void Initialize(HifBridgeState state, BridgeLogger logger)
        {
            _state = state;
            _logger = logger;
        }

        public bool Subscribe(PIClientSubscription clientSubscription)
        {
            if (_state == null)
                throw new InvalidOperationException("Bridge state is not initialized.");

            _state.RegisterSubscriber(clientSubscription);
            return true;
        }

        public List<PatientIdentity> SearchPatient(PISearchSpec query, int numberOfRecords = 0)
        {
            if (_state == null)
                throw new InvalidOperationException("Bridge state is not initialized.");

            return _state.SearchPatients(query, numberOfRecords);
        }

        public Guid Execute(PIChange change)
        {
            if (_state == null)
                throw new InvalidOperationException("Bridge state is not initialized.");

            _state.StorePatientChangeFromExecute(change);
            _logger.Info(string.Format("[HIF] Execute called: id={0}, trigger={1}, hl7Length={2}",
                change == null ? Guid.Empty : change.Id,
                change == null ? "(null)" : change.ChangeTrigger.ToString(),
                change == null || change.Descriptor == null || change.Descriptor.HL7Msg == null ? 0 : change.Descriptor.HL7Msg.Length));
            return change == null || change.Id == Guid.Empty ? Guid.NewGuid() : change.Id;
        }
    }
}
