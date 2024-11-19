# Open Commissioning Assistant Plugin for RobotStudio

### Description
Connects to a RobotStudio virtual controller instance using cyclic I/O communication and reading axis values.

### Quick Getting Started
- Download the zip file from the latest release page
- Unpack and place it in the directory or a subdirectory of the `OC.Assistant.exe`
- Start the Assistant and connect or create a TwinCAT solution
- Add a new plugin instance using the `+` button 
- Select `RobotStudio`, configure parameters and press `Apply` ([see also](https://github.com/OpenCommissioning/OC_Assistant?tab=readme-ov-file#installation-1))
- Depending on the parameters, a TwinCAT GVL with Robot In- and Outputs is generated  
- The plugin starts when TwinCAT goes to Run Mode and tries to connect to the virtual controller

### Plugin Parameters
- _AutoStart_: Automatic start and stop with TwinCAT
- _ControllerName_: Name of the virtual controller in RobotStudio
- _AxisNo_: Number of axes
- _CycleTime_: CycleTime in ms (to get a stable and evenly update rate, don't set this too low)
- _Solution_: Path of the RobotStudio solution file (*.rssln)

### Requirements
To run the plugin, you need RobotStudio 2024 installed on your system.\
See ABB documentation how to create and start a virtual robot controller using RobotStudio.

To build the plugin yourself, you need to copy the 
`ABB.Robotics.Controllers.PC.dll` to the project directory.\
You can find the dll in `C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\` by default.

### Notes
The plugin has been tested with RobotStudio Version `2024.2.1`
