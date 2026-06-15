using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using Philips.HIF.Contracts;

namespace PhilipsHifBridge
{
    internal sealed class HifBridgeState
    {
        private readonly object _sync = new object();
        private readonly BridgeLogger _logger;
        private readonly PatientStore _patientStore;
        private IPIClientCallback _callback;
        private ICommunicationObject _callbackChannel;
        private string _subscriberName;
        private DateTime _subscribedAt;
        private readonly List<PatientIdentity> _patients = new List<PatientIdentity>();
        private int _searchCount;
        private DateTime? _lastSearchAt;
        private DateTime? _lastPushAt;
        private string _lastPushResult;
        private int _loadedPatientCount;

        public HifBridgeState(BridgeLogger logger, PatientStore patientStore)
        {
            _logger = logger;
            _patientStore = patientStore;
        }

        public bool HasSubscriber
        {
            get
            {
                lock (_sync)
                    return IsSubscriberAliveLocked();
            }
        }

        public string SubscriberName
        {
            get
            {
                lock (_sync)
                    return _subscriberName ?? "";
            }
        }

        public int PatientCount
        {
            get
            {
                lock (_sync)
                    return _patients.Count;
            }
        }

        public string StatusText
        {
            get
            {
                lock (_sync)
                {
                    return string.Format("subscriber={0}; name={1}; subscribedAt={2}; patients={3}; loadedPatients={4}; searchCount={5}; lastSearchAt={6}; lastPushAt={7}; lastPushResult={8}; storageMode={9}; store={10}",
                        IsSubscriberAliveLocked(),
                        _subscriberName ?? "",
                        FormatTime(_subscribedAt == default(DateTime) ? (DateTime?)null : _subscribedAt),
                        _patients.Count,
                        _loadedPatientCount,
                        _searchCount,
                        FormatTime(_lastSearchAt),
                        FormatTime(_lastPushAt),
                        _lastPushResult ?? "",
                        _patientStore == null ? "" : _patientStore.StorageMode,
                        _patientStore == null ? "" : _patientStore.SafeStoreDescription);
                }
            }
        }

        public void LoadPersistedPatients()
        {
            if (_patientStore == null)
                return;

            var loaded = _patientStore.Load();
            lock (_sync)
            {
                _patients.Clear();
                foreach (var persisted in loaded)
                {
                    var patient = PiChangeFactory.CreatePatientIdentity(persisted);
                    if (patient != null)
                        _patients.Add(patient);
                }
                _loadedPatientCount = _patients.Count;
            }

            _logger.Info(string.Format("[STORE] Patient cache ready; loadedPatients={0}", _loadedPatientCount));
        }

        public void RegisterSubscriber(PIClientSubscription subscription)
        {
            var callback = OperationContext.Current.GetCallbackChannel<IPIClientCallback>();
            var channel = callback as ICommunicationObject;
            lock (_sync)
            {
                DetachCallbackChannelLocked();
                _callback = callback;
                _callbackChannel = channel;
                _subscriberName = subscription == null ? "(null)" : subscription.Name;
                _subscribedAt = DateTime.Now;
            }

            if (channel != null)
            {
                channel.Closed += OnSubscriberChannelEnded;
                channel.Faulted += OnSubscriberChannelEnded;
            }

            _logger.Info(string.Format("[HIF] PIC iX subscribed: name={0}, mode={1}, address={2}",
                subscription == null ? "(null)" : subscription.Name,
                subscription == null ? "(null)" : subscription.SubscriptionMode.ToString(),
                subscription == null || subscription.Address == null ? "(null)" : subscription.Address.ToString()));
        }

        private void OnSubscriberChannelEnded(object sender, EventArgs e)
        {
            var state = sender is ICommunicationObject comm ? comm.State.ToString() : "ended";
            ClearSubscriber("WCF channel " + state);
        }

        private void ClearSubscriber(string reason)
        {
            string name;
            lock (_sync)
            {
                name = _subscriberName ?? "";
                DetachCallbackChannelLocked();
                _callback = null;
                _callbackChannel = null;
                _subscriberName = null;
                _subscribedAt = default(DateTime);
            }

            if (!string.IsNullOrEmpty(name))
                _logger.Info(string.Format("[HIF] PIC iX subscriber disconnected ({0}); was name={1}", reason, name));
        }

        private void DetachCallbackChannelLocked()
        {
            if (_callbackChannel == null)
                return;

            _callbackChannel.Closed -= OnSubscriberChannelEnded;
            _callbackChannel.Faulted -= OnSubscriberChannelEnded;
            _callbackChannel = null;
        }

        private bool IsSubscriberAliveLocked()
        {
            if (_callback == null)
                return false;
            if (_callbackChannel == null)
                return true;
            return _callbackChannel.State == CommunicationState.Opened;
        }

        public bool PushToSubscriber(PIChange change, PIChangeDescriptor descriptor, out string result)
        {
            IPIClientCallback callback;
            string name;
            DateTime subscribedAt;
            lock (_sync)
            {
                StorePatientChange(change);
                if (!IsSubscriberAliveLocked())
                {
                    DetachCallbackChannelLocked();
                    _callback = null;
                    _subscriberName = null;
                    _subscribedAt = default(DateTime);
                    callback = null;
                    name = "";
                }
                else
                {
                    callback = _callback;
                    name = _subscriberName;
                    subscribedAt = _subscribedAt;
                }
            }

            if (callback == null)
            {
                result = "No PIC iX subscriber is connected.";
                return false;
            }

            try
            {
                _logger.Info(string.Format("[HIF] OnPIChange push: {0}", DescribeChange(change)));
                var accepted = callback.OnPIChange(change, descriptor);
                result = string.Format("OnPIChange returned {0}; subscriber={1}; subscribedAt={2:yyyy-MM-dd HH:mm:ss}",
                    accepted, name, subscribedAt);
                RememberPushResult(result);
                return accepted;
            }
            catch (FaultException<ExceptionDetail> ex)
            {
                result = FormatFaultException(ex);
                RememberPushResult(result);
                return false;
            }
            catch (FaultException ex)
            {
                result = ex.GetType().Name + ": " + ex.Message + "; code=" + ex.Code;
                RememberPushResult(result);
                return false;
            }
            catch (CommunicationException ex)
            {
                result = ex.GetType().Name + ": " + ex.Message;
                RememberPushResult(result);
                ClearSubscriber("push CommunicationException");
                return false;
            }
            catch (TimeoutException ex)
            {
                result = ex.GetType().Name + ": " + ex.Message;
                RememberPushResult(result);
                ClearSubscriber("push TimeoutException");
                return false;
            }
            catch (Exception ex)
            {
                result = ex.GetType().Name + ": " + ex.Message;
                RememberPushResult(result);
                if (IsChannelDeadException(ex))
                    ClearSubscriber("push failed");
                return false;
            }
        }

        private static bool IsChannelDeadException(Exception ex)
        {
            while (ex != null)
            {
                if (ex is CommunicationException || ex is TimeoutException)
                    return true;
                ex = ex.InnerException;
            }
            return false;
        }

        public List<PatientIdentity> SearchPatients(PISearchSpec query, int numberOfRecords)
        {
            lock (_sync)
            {
                _searchCount++;
                _lastSearchAt = DateTime.Now;
                var matched = FilterPatients(query);
                var results = numberOfRecords > 0
                    ? new List<PatientIdentity>(matched.Take(numberOfRecords))
                    : new List<PatientIdentity>(matched);
                _logger.Info(string.Format("[HIF] SearchPatient called; numberOfRecords={0}; returning {1} patient(s). Query={2}",
                    numberOfRecords,
                    results.Count,
                    DescribeSearchQuery(query)));
                return results;
            }
        }

        private List<PatientIdentity> FilterPatients(PISearchSpec query)
        {
            if (query == null || query.PatientIdentity == null)
                return new List<PatientIdentity>(_patients);

            var queryMrn = FindNumber(query.PatientIdentity, PtNumberFieldNames.MedicalRecordNumber.ToString());
            var queryFirstName = FindAttribute(query.PatientIdentity, PtAttributeFieldNames.FirstName.ToString());
            var queryLastName = FindAttribute(query.PatientIdentity, PtAttributeFieldNames.LastName.ToString());

            if (string.IsNullOrWhiteSpace(queryMrn) &&
                string.IsNullOrWhiteSpace(queryFirstName) &&
                string.IsNullOrWhiteSpace(queryLastName))
                return new List<PatientIdentity>(_patients);

            return _patients.Where(patient =>
            {
                if (!string.IsNullOrWhiteSpace(queryMrn))
                {
                    var mrn = FindNumber(patient, PtNumberFieldNames.MedicalRecordNumber.ToString());
                    if (!string.Equals(mrn, queryMrn, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(queryFirstName))
                {
                    var firstName = FindAttribute(patient, PtAttributeFieldNames.FirstName.ToString());
                    if (firstName.IndexOf(queryFirstName, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(queryLastName))
                {
                    var lastName = FindAttribute(patient, PtAttributeFieldNames.LastName.ToString());
                    if (lastName.IndexOf(queryLastName, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }

                return true;
            }).ToList();
        }

        public void StorePatientChangeFromExecute(PIChange change)
        {
            lock (_sync)
                StorePatientChange(change);
        }

        private void StorePatientChange(PIChange change)
        {
            if (change == null || change.After == null)
                return;

            var patient = change.After;
            var mrn = FindNumber(patient, PtNumberFieldNames.MedicalRecordNumber.ToString());
            var existingIndex = -1;
            if (!string.IsNullOrWhiteSpace(mrn))
                existingIndex = _patients.FindIndex(p => string.Equals(FindNumber(p, PtNumberFieldNames.MedicalRecordNumber.ToString()), mrn, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
                _patients[existingIndex] = patient;
            else
                _patients.Add(patient);

            SavePatientsLocked();

            _logger.Info(string.Format("[HIF] Stored patient: mrn={0}, name={1} {2}, careUnit={3}, room={4}, bed={5}, facility={6}, source={7}, total={8}",
                mrn,
                FindAttribute(patient, PtAttributeFieldNames.FirstName.ToString()),
                FindAttribute(patient, PtAttributeFieldNames.LastName.ToString()),
                FindLocation(patient, PtLocationFieldNames.CareUnit.ToString()),
                FindLocation(patient, PtLocationFieldNames.Room.ToString()),
                FindLocation(patient, PtLocationFieldNames.Bed.ToString()),
                FindLocation(patient, PtLocationFieldNames.Facility.ToString()),
                DescribeTransactionSource(patient),
                _patients.Count));
        }

        private void SavePatientsLocked()
        {
            if (_patientStore == null)
                return;

            var persisted = _patients
                .Select(PiChangeFactory.ToPersistedPatient)
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Mrn))
                .OrderBy(p => p.Mrn)
                .ToList();
            _patientStore.Save(persisted);
        }

        private static string DescribeChange(PIChange change)
        {
            if (change == null || change.After == null)
                return "(null)";

            var patient = change.After;
            return string.Format(
                "trigger={0}, uniqueId={1}, mrn={2}, name={3} {4}, careUnit={5}, room={6}, bed={7}, facility={8}, transactionSource={9}, changedAttrs={10}, changedNumbers={11}, changedLocations={12}",
                change.ChangeTrigger,
                patient.UniqueId,
                FindNumber(patient, PtNumberFieldNames.MedicalRecordNumber.ToString()),
                FindAttribute(patient, PtAttributeFieldNames.FirstName.ToString()),
                FindAttribute(patient, PtAttributeFieldNames.LastName.ToString()),
                FindLocation(patient, PtLocationFieldNames.CareUnit.ToString()),
                FindLocation(patient, PtLocationFieldNames.Room.ToString()),
                FindLocation(patient, PtLocationFieldNames.Bed.ToString()),
                FindLocation(patient, PtLocationFieldNames.Facility.ToString()),
                DescribeTransactionSource(patient),
                change.Descriptor == null || change.Descriptor.ChangedAttributes == null ? 0 : change.Descriptor.ChangedAttributes.Count,
                change.Descriptor == null || change.Descriptor.ChangedNumbers == null ? 0 : change.Descriptor.ChangedNumbers.Count,
                change.Descriptor == null || change.Descriptor.ChangedLocations == null ? 0 : change.Descriptor.ChangedLocations.Count);
        }

        private static string DescribeTransactionSource(PatientIdentity patient)
        {
            if (patient == null || patient.TransactionSource == null)
                return "";

            return patient.TransactionSource.Domain + "/" + patient.TransactionSource.Name;
        }

        private void RememberPushResult(string result)
        {
            lock (_sync)
            {
                _lastPushAt = DateTime.Now;
                _lastPushResult = result;
            }
        }

        private static string FormatTime(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        private static string DescribeSearchQuery(PISearchSpec query)
        {
            if (query == null || query.PatientIdentity == null)
                return "(null)";

            return string.Format("mrn={0}, name={1} {2}",
                FindNumber(query.PatientIdentity, PtNumberFieldNames.MedicalRecordNumber.ToString()),
                FindAttribute(query.PatientIdentity, PtAttributeFieldNames.FirstName.ToString()),
                FindAttribute(query.PatientIdentity, PtAttributeFieldNames.LastName.ToString()));
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

        private static string FindLocation(PatientIdentity patient, string fieldName)
        {
            if (patient == null || patient.Locations == null)
                return "";

            var location = patient.Locations.FirstOrDefault(l => string.Equals(l.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            return location == null ? "" : location.Value;
        }

        private static string FormatFaultException(FaultException<ExceptionDetail> ex)
        {
            var builder = new StringBuilder();
            builder.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            builder.Append("; detail=").Append(FormatExceptionDetail(ex.Detail));
            return builder.ToString();
        }

        private static string FormatExceptionDetail(ExceptionDetail detail)
        {
            if (detail == null)
                return "(null)";

            var builder = new StringBuilder();
            var current = detail;
            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Append(" -> ");

                builder.Append(current.Type).Append(": ").Append(current.Message);
                current = current.InnerException;
            }

            return builder.ToString();
        }
    }
}
