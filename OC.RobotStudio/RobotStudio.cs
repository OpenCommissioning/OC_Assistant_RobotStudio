using System.Collections;
using System.Diagnostics;
using ABB.Robotics.Controllers;
using ABB.Robotics.Controllers.Discovery;
using ABB.Robotics.Controllers.IOSystemDomain;
using ABB.Robotics.Controllers.MotionDomain;
using OC.Assistant.Sdk;
using OC.Assistant.Sdk.Plugin;

namespace OC.RobotStudio;

[PluginIoType(IoType.Struct)]
public class RobotStudio : PluginBase
{
    [PluginParameter("Name of the controller in RobotStudio")]
    private readonly string _controllerName = "Robot1";
        
    [PluginParameter("Number of axes")]
    private readonly int _axisNo = 6;
        
    [PluginParameter("CycleTime in ms \ndefault: 50")]
    private readonly int _cycleTime = 50;
        
    [PluginParameter("Path of the RobotStudio solution file", "rssln")]
    private readonly string _solution = @"C:\Users\john.doe\Documents\RobotStudio\Solutions\RobotStation1";
        
    private readonly Dictionary<string, IoSignal> _inputs = new();
    private readonly Dictionary<string, IoSignal> _outputs = new();
    private float[] _axisValues = [];
    private ControllerInfo[]? _virtualControllers;
    private Controller? _abbController;
    private IEnumerable<IoDevice>? _ioDevices;
    private Task? _inputWriter;
    private Task? _axisReader;
    private Interpolator[] _smoothing = [];

    protected override bool OnSave()
    {
        try
        {
            var solutionFolder = Path.GetDirectoryName(_solution);
            var eioCfg = CfgParser.FindAndConvert(solutionFolder);
            if (eioCfg is null)
            {
                Logger.LogError(this, $"{(solutionFolder == "" ? "Project folder must not be empty" : $"Error reading EIO file in {solutionFolder}")}");
                return false;
            }

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

    protected override bool OnStart()
    {
        _virtualControllers = GetControllers(CancellationToken);
            
        if (_virtualControllers.Length == 0)
        {
            Logger.LogWarning(this, $"Connecting to robot '{_controllerName}' aborted");
            return false;
        }

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
    }

    private void SetOutputStructure()
    {
        _outputs.Clear();
        OutputStructure.AddVariable("Axis", TcType.Real, _axisNo);
        DevicesToStruct(OutputStructure, _ioDevices, _outputs, false);
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
                    _ => TcType.Byte
                };

                if (signal.IoItem?.Name is null) continue;
                ioSignals.Add(signal.IoItem.Name, signal);
                structure.AddVariable($"(*{signal.Index}*) {adsName}", adsType);
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
                bitBuffer[bitOffset] = signal.Bool;
                bitOffset += bitSize;
                continue;
            }

            //is Group- or AnalogValue (8, 16, 32 Bit)
            var mod = bitOffset % 8;
            if (mod != 0) bitOffset += 8 - mod;
            var bits = signal.BitArray;
            for (var i = 0; i < bitSize; i++) bitBuffer[bitOffset + i] = bits[i];
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
    private ControllerInfo[] GetControllers(CancellationToken token)
    {
        Logger.LogInfo(this, $"Waiting for connection to robot '{_controllerName}'...");
        var controllers = Array.Empty<ControllerInfo>();

        //waiting for RobotStudio
        while (controllers.Length == 0 && !token.IsCancellationRequested)
        {
            try
            {
                var scanner = new NetworkScanner();
                scanner.Scan();
                controllers = scanner.GetControllers(NetworkScannerSearchCriterias.Virtual);
                break;
            }
            catch(Exception e)
            {
                Logger.LogError(this, e.Message);
                if (e.InnerException?.Message is not null)
                {
                    Logger.LogError(this, e.InnerException?.Message ?? "");
                }
            }

            //Wait a sec
            Thread.Sleep(2000);

            controllers = [];
        }

        return controllers;
    }

    /// <summary>
    /// Connect the virtual controller by name and subscribe to signals
    /// </summary>
    private bool Connect()
    {
        var controllerInfo = _virtualControllers?.FirstOrDefault(x => 
            string.Equals(x.Name, _controllerName, StringComparison.CurrentCultureIgnoreCase));
            
        if (controllerInfo != default)
        {
            try
            {
                _abbController = Controller.Connect(controllerInfo.SystemId, ConnectionType.Standalone);
                _abbController.Logon(UserInfo.DefaultUser);
                    
                Logger.LogInfo(this, "Loading controller configuration...");
                var mappedInputs = MapSignals(_abbController, IOFilterTypes.Input, _inputs);
                var mappedOutputs = MapSignals(_abbController, IOFilterTypes.Output, _outputs);
                Logger.LogInfo(this, $"Mapped {mappedInputs} inputs and {mappedOutputs} outputs");
                    
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
        if (_abbController is not null && _abbController.Connected)
        {
            _abbController.Logoff();
            _abbController.Dispose();
        }
            
        _abbController = null;
        _virtualControllers = null;
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
            if (!dictionary.TryGetValue(signal.Name, out var value)) continue;
            value.Signal = signal;
            mappedSignals++;
        }
            
        return mappedSignals;
    }
}