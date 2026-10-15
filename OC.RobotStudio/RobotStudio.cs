using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.Discovery;
using ABB.Robotics.Controllers.IOSystemDomain;
using ABB.Robotics.Controllers.MotionDomain;
using OC.Assistant.Sdk;
using OC.Assistant.Sdk.Plugin;

namespace OC.RobotStudio;

public class RobotStudio : PluginBase
{
    [PluginParameter("Name of the controller in RobotStudio")]
    private readonly string _controllerName = "Robot1";
        
    [PluginParameter("Number of axes")]
    private readonly int _axisNo = 6;
        
    [PluginParameter("CycleTime in ms \ndefault: 50")]
    private readonly int _cycleTime = 50;
        
    private readonly Dictionary<string, IoSignal> _inputs = new();
    private readonly Dictionary<string, IoSignal> _outputs = new();
    private float[] _axisValues = [];
    private ControllerInfo[] _controllers = [];
    private Controller? _abbController;
    private IEnumerable<IoDevice>? _ioDevices;
    private Task? _inputWriter;
    private Task? _axisReader;
    private Interpolator[] _smoothing = [];
    private readonly List<Unit> _outputUnits = [];
    private readonly List<IoSignal> _safetyInputs = [];
    private readonly List<IoSignal> _safetyOutputs = [];
    private static readonly PropertyInfo UnitInternalProperty =
        typeof(Unit).GetProperty("InternalUnit", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(Unit), "InternalUnit");
    private static readonly MethodInfo GetAdapterMethod =
        UnitInternalProperty.PropertyType.GetMethod("GetAdapter", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(UnitInternalProperty.PropertyType.FullName, "GetAdapter");
    private static readonly MethodInfo ReadOutputDataMethod =
        GetAdapterMethod.ReturnType.GetMethod("ReadOutputData", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new MissingMemberException(GetAdapterMethod.ReturnType.FullName, "ReadOutputData");
    private static readonly FieldInfo OutputDataField =
        ReadOutputDataMethod.ReturnType.GetGenericArguments()[0].GetField("data")
        ?? throw new MissingFieldException(ReadOutputDataMethod.ReturnType.GetGenericArguments()[0].FullName, "data");
    private static readonly FieldInfo OutputMaskField =
        ReadOutputDataMethod.ReturnType.GetGenericArguments()[0].GetField("mask")
        ?? throw new MissingFieldException(ReadOutputDataMethod.ReturnType.GetGenericArguments()[0].FullName, "mask");

    protected override bool OnSave()
    {
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(AppData.Path, nameof(RobotStudio), Name ?? "unnamed"));
            var path = Path.Combine(dir.FullName, $"{_controllerName}_EIO.cfg");
            
            if (!SaveEioCfg(path)) return false;
            var eioCfg = CfgParser.ConvertToXml(path);
            if (eioCfg is null) return false;

            LoadSafetySignals();
            _ioDevices = eioCfg.GetAllDevices();
            SetInputStructure();
            SetOutputStructure();
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }
        
        return true;
    }

    private bool SaveEioCfg(string path)
    {
        try
        {
            _controllers = GetControllers();
            
            var controllerInfo = _controllers.FirstOrDefault(x => 
                string.Equals(x.Name, _controllerName, StringComparison.CurrentCultureIgnoreCase));

            if (controllerInfo is null)
            {
                Logger.LogWarning(this, $"Controller '{_controllerName}' not found");
                return false;
            }
            
            using var controller = Controller.Connect(controllerInfo.SystemId, ConnectionType.Standalone);
            controller.Configuration.ExternalIO.Save(path);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }
    }

    protected override bool OnStart()
    {
        if (!Connect())
        {
            return false;
        }
            
        //Start tasks
        _inputWriter = InputWriterTask(CancellationToken);
        _axisReader = AxisReaderTask(CancellationToken);
            
        _smoothing = new Interpolator[_axisNo];
        for (var i = 0; i < _smoothing.Length; i++) _smoothing[i] = new Interpolator(_cycleTime / 1000.0);

        return true;
    }

    protected override void OnUpdate()
    {
        try
        {
            if (_abbController is null)
            {
                CancellationRequest();
                return;
            }

            if (!_abbController.Connected)
            {
                CancellationRequest();
                return;
            }
                
            for (var i = 0; i < _axisNo; i++)
            {
                var value = BitConverter.GetBytes((float)_smoothing[i].Calculate(_axisValues[i]));
                Array.Copy(value, 0, OutputBuffer, i * 4, 4);
            }
                
            WriteToBuffer(_outputs.Values, OutputBuffer, _axisNo * 4);
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            CancellationRequest();
        }
    }

    protected override void OnStop()
    {
        //Wait for all tasks
        _inputWriter?.Wait();
        _axisReader?.Wait();
            
        //Disconnect controller
        Disconnect();
    }
        
    private void SetInputStructure()
    {
        _inputs.Clear();
        DevicesToStruct(InputStructure, _ioDevices, _inputs, true);
        AddSafetyInputs();
    }

    private void SetOutputStructure()
    {
        _outputs.Clear();
        OutputStructure.AddVariable("Axis", TcType.Real, _axisNo);
        DevicesToStruct(OutputStructure, _ioDevices, _outputs, false);
        AddSafetyOutputs();
        _axisValues = new float[_axisNo];
    }

    private static void DevicesToStruct(
        IIoStructure structure, 
        IEnumerable<IoDevice>? ioDevices, 
        IDictionary<string, IoSignal> ioSignals, 
        bool isInput)
    {
        if (ioDevices is null) return;

        foreach (var ioDevice in ioDevices)
        {
            var signals = isInput ? ioDevice.Inputs : ioDevice.Outputs;

            if (signals is null) return;
            
            foreach (var signal in signals)
            {
                var adsName = $"{ioDevice?.DeviceItem?.Name}_{signal.IoItem?.Name}";
                var adsType = signal.Length switch
                {
                    1 => TcType.Bit,
                    8 => TcType.Byte,
                    16 => TcType.Word,
                    32 => TcType.Dword,
                    _ => TcType.Unknown
                };

                if (signal.IoItem?.Name is null || adsType == TcType.Unknown) continue;
                ioSignals.Add(signal.IoItem.Name, signal);
                structure.AddVariable(adsName, adsType);
            }

            if (ioDevice?.HasSubModules == true) DevicesToStruct(structure, ioDevice.SubModules, ioSignals, isInput);
        }
    }
        
    /// <summary>
    /// Write signal values to a buffer
    /// </summary>
    private static void WriteToBuffer(IEnumerable<IoSignal> signals, byte[] buffer, int offset)
    {
        var bitOffset = offset * 8;
        var bitBuffer = new BitArray(buffer);
            
        foreach (var signal in signals)
        {
            var bitSize = signal.Length;

            if (!signal.IsValid)
            {
                bitOffset += bitSize;
                continue;
            }
                
            //is bool
            if (bitSize == 1)
            {
                bitBuffer[bitOffset] = signal.ValueAsBool;
                bitOffset += bitSize;
                continue;
            }

            //is Group- or AnalogValue (8, 16, 32 Bit)
            var mod = bitOffset % 8;
            if (mod != 0) bitOffset += 8 - mod;
            var bits = signal.ValueAsUInt32Bits;
            for (var i = 0; i < bitSize; i++) bitBuffer[bitOffset + i] = ((bits >> i) & 1) != 0;
            bitOffset += bitSize;
        }
            
        bitBuffer.CopyTo(buffer, 0);
    }

    /// <summary>
    /// Read signal values from a buffer
    /// </summary>
    private static void ReadFromBuffer(IEnumerable<IoSignal> signals, byte[] buffer, int offset)
    {
        var bitOffset = offset * 8;
        var bitBuffer = new BitArray(buffer);
            
        foreach (var signal in signals.Where(signal => signal.IsValid))
        {
            var bitSize = signal.Length;
            
            //is bool
            if (bitSize == 1)
            {
                bitOffset++;
                signal.Value = bitBuffer[bitOffset - 1] ? 1.0f : 0.0f;
                continue;
            }

            //is Group- or AnalogValue (8, 16, 32 Bit)
            var mod = bitOffset % 8;
            if (mod != 0) bitOffset += 8 - mod;
            var bits = new BitArray(bitSize);
            for (var i = 0; i < bitSize; i++) bits[i] = bitBuffer[bitOffset + i];
            var bytes = new byte[4];
            bits.CopyTo(bytes, 0);
            bitOffset += bitSize;
            signal.Value = BitConverter.ToUInt32(bytes, 0);
        }
    }
        
    private async Task InputWriterTask(CancellationToken token)
    {
        await Task.Run(() =>
        {
            var stopwatch = new StopwatchEx();

            while (!token.IsCancellationRequested)
            {
                stopwatch.WaitUntil(_cycleTime);
                    
                if (_abbController is null) break;
                if (!_abbController.Connected) break;
                    
                try
                {
                    ReadFromBuffer(_inputs.Values, InputBuffer, 0);
                }
                catch (Exception e)
                {
                    Logger.LogError(this, $"<{nameof(InputWriterTask)}> {e.Message}");
                    CancellationRequest();
                    break;
                }
            }
        }, token);
    }
        
    private async Task AxisReaderTask(CancellationToken token)
    {
        await Task.Run(() =>
        {
            var stopwatch = new StopwatchEx();
            var axisValues = new AxisValues();
            var mechanicalUnits = _abbController?.MotionSystem.MechanicalUnits;
            var logCounter = 0;
            var imagesPerSecond = 0;
            string? imageError = null;

            while (!token.IsCancellationRequested)
            {
                stopwatch.WaitUntil(_cycleTime);
                    
                if (_abbController is null) break;
                if (!_abbController.Connected) break;

                try 
                {
                    var index = 0;

                    foreach (MechanicalUnit mechanicalUnit in mechanicalUnits ?? [])
                    {
                        axisValues.Set(mechanicalUnit);
                            
                        for (var i = 0; i < axisValues.NumberOfAxes; i++, index++)
                        {
                            if (index < _axisValues.Length) _axisValues[index] = axisValues.Axis[i];
                        }
                    }

                    var images = 0;
                    foreach (var unit in _outputUnits)
                    {
                        try
                        {
                            if (!TryReadOutputImage(unit, out var data, out var mask))
                            {
                                imageError = $"empty output image for unit '{unit.Name}'";
                                continue;
                            }

                            images++;
                            foreach (var signal in _outputs.Values)
                            {
                                if (!string.Equals(signal.UnitName, unit.Name, StringComparison.OrdinalIgnoreCase)) continue;
                                signal.ApplyOutputImage(data, mask);
                            }
                        }
                        catch (Exception imageEx)
                        {
                            imageError = imageEx.InnerException?.Message ?? imageEx.Message;
                        }
                    }

                    imagesPerSecond += images;

                    var readMs = stopwatch.Elapsed.TotalMilliseconds;
                    if (++logCounter >= 1000 / Math.Max(_cycleTime, 1))
                    {
                        logCounter = 0;
                        var error = imageError is null ? "" : $", last image error: {imageError}";
                        Logger.LogInfo(this, $"Read cycle {readMs:0.0} ms, setting {_cycleTime} ms, {imagesPerSecond} output images/s{error}", true);
                        imagesPerSecond = 0;
                        imageError = null;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(this, $"<{nameof(AxisReaderTask)}> {e.Message}");
                    CancellationRequest();
                    break;
                }
            }
        }, token);
    }
        
    /// <summary>
    /// Scan RobotStudio and get controllers
    /// </summary>
    private ControllerInfo[] GetControllers()
    {
        var controllers = Array.Empty<ControllerInfo>();
        
        try
        {
            var scanner = new NetworkScanner();
            scanner.Scan();
            controllers = scanner.GetControllers(NetworkScannerSearchCriterias.Virtual);
        }
        catch(Exception e)
        {
            Logger.LogError(this, e.Message);
            CancellationRequest();
        }
            
        return controllers;
    }

    /// <summary>
    /// Connect the virtual controller by name and subscribe to signals
    /// </summary>
    private bool Connect()
    {
        if (_controllers.Length == 0)
        {
            Logger.LogWarning(this, $"No controllers found. Connecting to robot '{_controllerName}' aborted");
            return false;
        }
        
        var controllerInfo = _controllers.FirstOrDefault(x => 
            string.Equals(x.Name, _controllerName, StringComparison.CurrentCultureIgnoreCase));
            
        if (controllerInfo != null)
        {
            try
            {
                _abbController = Controller.Connect(controllerInfo.SystemId, ConnectionType.Standalone);
                _abbController.Logon(UserInfo.DefaultUser);
                    
                Logger.LogInfo(this, "Loading controller configuration...");
                var mappedInputs = MapSignals(_abbController, IOFilterTypes.Input, _inputs);
                var mappedOutputs = MapSignals(_abbController, IOFilterTypes.Output, _outputs);
                Logger.LogInfo(this, $"Mapped {mappedInputs} inputs and {mappedOutputs} outputs");
                LoadOutputUnits();
                    
            }
            catch (Exception e)
            {
                Logger.LogError(this, $"<{nameof(Connect)}()> init error: {e.Message}");
                return false;
            }
        }
        else
        {
            Logger.LogError(this, $"Could not connect to robot '{_controllerName}'");
            return false;
        }

        Logger.LogInfo(this, $"Connected to robot '{_controllerName}'");
        return true;
    }
        
    /// <summary>
    /// Disconnect the virtual controller
    /// </summary>
    private void Disconnect()
    {
        _outputUnits.Clear();

        if (_abbController is not null && _abbController.Connected)
        {
            _abbController.Logoff();
            _abbController.Dispose();
        }
            
        _abbController = null;
        Logger.LogInfo(this, $"Robot '{_controllerName}' disconnected");
    }
        
    /// <summary>
    /// Map signals from EIO.cfg to controller
    /// </summary>
    private static int MapSignals(Controller ctrl, IOFilterTypes filter, Dictionary<string, IoSignal> dictionary)
    {
        var signals = ctrl.IOSystem.GetSignals(filter);
        if (signals is null) return 0;

        var mappedSignals = 0;
        foreach (Signal signal in signals)
        {
            if (!dictionary.TryGetValue(signal.Name, out var ioSignal)) continue;
            ioSignal.Map(signal);
            mappedSignals++;
        }
            
        return mappedSignals;
    }

    private void LoadSafetySignals()
    {
        _safetyInputs.Clear();
        _safetyOutputs.Clear();
        var path = FindSafetyCfg();
        if (path is null)
        {
            Logger.LogInfo(this, "No safety I/O configuration found");
            return;
        }

        var text = File.ReadAllText(path).Replace("\\\r\n", " ").Replace("\\\n", " ");
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var signalType = QuotedValue(line, "-SignalType ");
            if (signalType is not ("DO" or "DI" or "GI")) continue;
            if (signalType is "DI" or "GI" &&
                !line.Contains("-Category \"ProfiSafe\"", StringComparison.Ordinal)) continue;

            var name = QuotedValue(line, "-Name ");
            var device = QuotedValue(line, "-Device ");
            var deviceMap = QuotedValue(line, "-DeviceMap ");
            if (name is null || device is null || deviceMap is null) continue;

            var item = new XElement("Item",
                new XAttribute("Name", name),
                new XAttribute("SignalType", signalType),
                new XAttribute("Device", device),
                new XAttribute("DeviceMap", deviceMap));
            var signal = new IoSignal(item);
            if (signalType == "DO") _safetyOutputs.Add(signal);
            else _safetyInputs.Add(signal);
        }

        Logger.LogInfo(this, $"Found {_safetyOutputs.Count} safety outputs in '{path}'");
        Logger.LogInfo(this, $"Found {_safetyInputs.Count} safety inputs in '{path}'");
        Logger.LogInfo(this, "Safety outputs: " + string.Join(", ", _safetyOutputs.Select(signal => signal.IoItem?.Name)), true);
        Logger.LogInfo(this, "Safety inputs: " + string.Join(", ", _safetyInputs.Select(signal => signal.IoItem?.Name)), true);
    }

    private static string? QuotedValue(string line, string key)
    {
        var keyIndex = line.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0) return null;

        var start = line.IndexOf('"', keyIndex + key.Length);
        if (start < 0) return null;

        var end = line.IndexOf('"', start + 1);
        if (end < 0) return null;

        return line[(start + 1)..end];
    }

    private string? FindSafetyCfg()
    {
        var controllerInfo = _controllers.FirstOrDefault(x =>
            string.Equals(x.Name, _controllerName, StringComparison.CurrentCultureIgnoreCase));
        DirectoryInfo?[] directories = [controllerInfo?.BaseDirectory, controllerInfo?.BaseDirectory?.Parent];

        foreach (var directory in directories)
        {
            if (directory is null || !directory.Exists) continue;
            var path = Path.Combine(directory.FullName, "INTERNAL", "OPTIONS", "SC", "sc_eio.cfg");
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private void AddSafetyInputs()
    {
        var added = 0;
        foreach (var signal in _safetyInputs.OrderBy(item => item.Index))
        {
            var name = signal.IoItem?.Name;
            var device = signal.IoItem?.Device;
            var adsType = signal.Length switch
            {
                1 => TcType.Bit,
                8 => TcType.Byte,
                16 => TcType.Word,
                32 => TcType.Dword,
                _ => TcType.Unknown
            };
            if (name is null || device is null || adsType == TcType.Unknown || _inputs.ContainsKey(name)) continue;

            _inputs.Add(name, signal);
            InputStructure.AddVariable($"{device}_{name}", adsType);
            added++;
        }

        if (added > 0) Logger.LogInfo(this, $"Added {added} safety inputs");
    }

    private void AddSafetyOutputs()
    {
        var added = 0;
        foreach (var signal in _safetyOutputs.OrderBy(item => item.Index))
        {
            var name = signal.IoItem?.Name;
            var device = signal.IoItem?.Device;
            if (name is null || device is null || signal.Length != 1 || _outputs.ContainsKey(name)) continue;

            _outputs.Add(name, signal);
            OutputStructure.AddVariable($"{device}_{name}", TcType.Bit);
            added++;
        }

        if (added > 0) Logger.LogInfo(this, $"Added {added} safety outputs");
    }

    private void LoadOutputUnits()
    {
        if (_abbController is null) return;

        var unitNames = _outputs.Values
            .Select(signal => signal.UnitName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var unitName in unitNames)
        {
            try
            {
                _outputUnits.Add(_abbController.IOSystem.GetUnit(unitName));
                Logger.LogInfo(this, $"Reading output image for unit '{unitName}'");
            }
            catch (Exception e)
            {
                Logger.LogError(this, $"Could not get unit '{unitName}': {e.Message}");
            }
        }
    }

    /// <summary>
    /// Reads the current output image of one unit. The PC SDK does not expose this
    /// on <see cref="Unit"/>, so the read goes through the same adapter the SDK uses internally.
    /// </summary>
    private static bool TryReadOutputImage(Unit unit, out byte[] data, out byte[] mask)
    {
        data = [];
        mask = [];

        var internalUnit = UnitInternalProperty.GetValue(unit);
        if (internalUnit is null) return false;

        var adapter = GetAdapterMethod.Invoke(internalUnit, null);
        if (adapter is null) return false;

        if (ReadOutputDataMethod.Invoke(adapter, null) is not IList bytes || bytes.Count == 0) return false;

        data = new byte[bytes.Count];
        mask = new byte[bytes.Count];
        for (var i = 0; i < bytes.Count; i++)
        {
            var item = bytes[i];
            if (item is null) continue;
            data[i] = (byte)(OutputDataField.GetValue(item) ?? 0);
            mask[i] = (byte)(OutputMaskField.GetValue(item) ?? 0);
        }

        return true;
    }
}