using System.Collections;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using ABB.Robotics.Controllers.IOSystemDomain;
using OC.Assistant.Sdk;

namespace OC.RobotStudio;

internal class IoSignal
{
    private const float TOLERANCE = 1.0E-9f;
    private Signal? _signal;
    private readonly int _index;
    private float _value;
    public IoItem? IoItem { get; }
    public int Index => _index;
    public int Length { get; }
    public Signal? Signal
    {
        set
        {
            _signal = value;
            InitializeSignal();
        }
    }

    public bool IsValid => _signal is not null;
        
    public IoSignal(XNode signal)
    {
        var serializer = new XmlSerializer(typeof(IoItem));
        using (var reader = signal.CreateReader())
        {
            if (serializer.CanDeserialize(reader))
            {
                IoItem = serializer.Deserialize(reader) as IoItem;
            }

            if (IoItem is null)
            {
                Logger.LogError(this, "Deserialization error");
                return;
            }
        }

        if (IoItem.DeviceMap is null) return;
        var m = Regex.Match(IoItem.DeviceMap, @"(?<LOW>\d+)-(?<HIGH>\d+)");
        if (m.Success)
        {
            var low = int.Parse(m.Groups["LOW"].Value);
            var high = int.Parse(m.Groups["HIGH"].Value);
            Length = high - low + 1;
            _index = low;
        }
        else
        {
            int.TryParse(IoItem.DeviceMap, out _index);
            Length = 1;
        }
    }

    public bool Bool => Math.Abs(_value) > 0.5f;
    public BitArray BitArray => new(BitConverter.GetBytes((uint)_value));

    public float Value
    {
        get => _value;
        set
        {
            if (Math.Abs(value - _value) < TOLERANCE) return;
            _value = value;
            try
            {
                if (_signal is null) return;
                _signal.Value = value;
            }
            catch (Exception e)
            {
                Logger.LogError(this, e.Message);
            }
        }
    }

    private void InitializeSignal()
    {
        if (_signal is null) return;
        switch (_signal.Type)
        {
            case SignalType.AnalogOutput:
            case SignalType.DigitalOutput:
            case SignalType.GroupOutput:
                _signal.Changed += SignalOnChanged;
                break;
            case SignalType.DigitalInput:
            case SignalType.GroupInput:
            case SignalType.AnalogInput:
                _signal.Changed += SignalOnChanged;
                _signal.InputAsPhysical = true;
                _signal.Value = 0f;
                break;
            case SignalType.Unknown:
            default:
                Logger.LogWarning(this, $"Signal type '{_signal.Type}' for signal '{_signal.Name}' not supported");
                break;
        }
    }

    private void SignalOnChanged(object? sender, SignalChangedEventArgs e)
    {
        try
        {
            _value = e.NewSignalState.Value;
            Logger.LogInfo(this, $"Signal '{_signal?.Name}' changed value to {_value}", true);
        }
        catch (Exception ex)
        {
            Logger.LogError(this, $"error: {ex.Message}");
        }
    }
}