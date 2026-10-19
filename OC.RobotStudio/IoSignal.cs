using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.IOSystemDomain;
using OC.Assistant.Sdk;

namespace OC.RobotStudio;

internal partial class IoSignal
{
    private const float TOLERANCE = 1.0E-9f;
    private Signal? _signal;
    private readonly int _index;
    private float _value;
    public IoItem? IoItem { get; }
    public int Index => _index;
    public int Length { get; }
    public string? UnitName { get; private set; }

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
        var ranges = AddressRangeRegex().Matches(IoItem.DeviceMap);
        if (ranges.Count > 0)
        {
            var length = 0;
            var index = int.MaxValue;
            foreach (Match range in ranges)
            {
                var low = int.Parse(range.Groups["LOW"].Value);
                var high = int.Parse(range.Groups["HIGH"].Value);
                length += high - low + 1;
                if (low < index) index = low;
            }

            var mod = length % 8;
            if (mod != 0) length += 8 - mod;
            Length = length;
            _index = index;
        }
        else
        {
            int.TryParse(IoItem.DeviceMap, out _index);
            Length = 1;
        }
    }
    
    public bool ValueAsBool => Math.Abs(_value) > 0.5f;
    public uint ValueAsUInt32Bits => _signal?.Type is SignalType.GroupOutput or SignalType.GroupInput or SignalType.AnalogOutput
        ? (uint)Math.Round(Math.Max(_value, 0f))
        : BitConverter.SingleToUInt32Bits(_value);

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

    /// <summary>
    /// Applies one unit output image. Does not write back to the robot.
    /// Bits whose mask is clear are left unchanged.
    /// </summary>
    public void ApplyOutputImage(byte[] data, byte[] mask)
    {
        if (Length <= 0) return;

        var value = (uint)Math.Round(Math.Max(_value, 0f));
        var applied = false;

        for (var i = 0; i < Length && i < 32; i++)
        {
            var map = Index + i;
            var byteIndex = map / 8;
            var bitIndex = map % 8;
            if ((uint)byteIndex >= (uint)data.Length || (uint)byteIndex >= (uint)mask.Length) return;

            var maskByte = mask[byteIndex];
            if (((maskByte >> bitIndex) & 1) == 0) continue;

            applied = true;
            var dataBit = ((data[byteIndex] >> bitIndex) & 1) != 0;
            if (dataBit) value |= 1u << i;
            else value &= ~(1u << i);
        }

        if (applied) _value = value;
    }

    public void Map(Signal signal)
    {
        
        _signal = signal;
        UnitName = _signal.Unit;
        switch (_signal.Type)
        {
            case SignalType.AnalogOutput:
            case SignalType.DigitalOutput:
            case SignalType.GroupOutput:
                _value = _signal.Value;
                break;
            case SignalType.DigitalInput:
            case SignalType.GroupInput:
            case SignalType.AnalogInput:
                //_signal.Changed += SignalOnChanged;
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

    [GeneratedRegex(@"(?<LOW>\d+)-(?<HIGH>\d+)")]
    private static partial Regex AddressRangeRegex();
}