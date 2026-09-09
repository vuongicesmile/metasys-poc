using BmsIngestionApp.Models;

namespace BmsIngestionApp.Services;

public sealed class IngestionStatusTracker
{
    private const int RecentEventLimit = 25;
    private readonly object _sync = new();
    private readonly Queue<CovEvent> _recentEvents = new();
    private string _state = "Starting";
    private string _metasysBaseUrl = "";
    private bool _sqlEnabled;
    private string? _subscriptionId;
    private long _eventsReceived;
    private long _rowsInserted;
    private int _buildingsUpserted;
    private int _equipmentUpserted;
    private DateTime? _startedAt;
    private DateTime? _lastEventAt;
    private CovEvent? _lastEvent;
    private string? _error;

    public void Configure(string metasysBaseUrl, bool sqlEnabled)
    {
        lock (_sync)
        {
            _metasysBaseUrl = metasysBaseUrl;
            _sqlEnabled = sqlEnabled;
            _startedAt = DateTime.UtcNow;
            _state = "Connecting";
        }
    }

    public void Connected(string subscriptionId)
    {
        lock (_sync)
        {
            _subscriptionId = subscriptionId;
            _state = "Listening";
            _error = null;
        }
    }

    public void EventReceived(CovEvent covEvent)
    {
        lock (_sync)
        {
            _eventsReceived++;
            _lastEvent = covEvent;
            _lastEventAt = DateTime.UtcNow;
            _recentEvents.Enqueue(covEvent);

            while (_recentEvents.Count > RecentEventLimit)
            {
                _recentEvents.Dequeue();
            }
        }
    }

    public void RowInserted()
    {
        lock (_sync)
        {
            _rowsInserted++;
        }
    }

    public void CatalogPersisted(int buildings, int equipment)
    {
        lock (_sync) { _buildingsUpserted = buildings; _equipmentUpserted = equipment; }
    }

    public void Failed(Exception exception)
    {
        lock (_sync)
        {
            _state = "Failed";
            _error = exception.Message;
        }
    }

    public IngestionStatusSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new IngestionStatusSnapshot
            {
                State = _state,
                MetasysBaseUrl = _metasysBaseUrl,
                SqlEnabled = _sqlEnabled,
                SubscriptionId = _subscriptionId,
                EventsReceived = _eventsReceived,
                RowsInserted = _rowsInserted,
                BuildingsUpserted = _buildingsUpserted,
                EquipmentUpserted = _equipmentUpserted,
                StartedAt = _startedAt,
                LastEventAt = _lastEventAt,
                LastEvent = _lastEvent,
                Error = _error
            };
        }
    }

    public IReadOnlyList<CovEvent> GetRecentEvents()
    {
        lock (_sync)
        {
            return _recentEvents.Reverse().ToArray();
        }
    }
}
