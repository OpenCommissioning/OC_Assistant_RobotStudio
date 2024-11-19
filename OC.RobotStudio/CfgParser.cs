using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OC.Assistant.Sdk;

namespace OC.RobotStudio;

/// <summary>
/// Static class to parse a RobotStudio EIO.cfg file
/// </summary>
internal static class CfgParser
{
    private static readonly string[] DeviceTypes =
    [
        "PROFINET_INTERNAL_DEVICE", 
        "PROFINET_DEVICE", 
        "DEVICENET_DEVICE", 
        "ETHERNETIP_DEVICE"
    ];
        
    /// <summary>
    /// Find the EIO.cfg and parse to xml
    /// </summary>
    public static XDocument? FindAndConvert(string? directory)
    {
        var file = FindCfgFile(directory);
        try
        {
            if (!string.IsNullOrEmpty(file)) return ConvertToXml(file);
            Logger.LogError(typeof(CfgParser), $"Directory '{directory}' not found");
        }
        catch (Exception e)
        {
            Logger.LogError(typeof(CfgParser), e.Message);
        }
        return null;
    }

    /// <summary>
    /// Find the EIO.cfg file
    /// </summary>
    /// <returns>Path to EIO.cfg</returns>
    private static string FindCfgFile(string? directory)
    {
        const string fileFilter = "EIO.cfg";

        try
        {
            if (Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, fileFilter, SearchOption.AllDirectories);
                if (files.Length != 0) return files.First();
                Logger.LogError(typeof(CfgParser), $"File '{fileFilter}' not found in directory '{directory}'");
            }
        }
        catch (Exception e)
        {
            Logger.LogError(typeof(CfgParser), e.Message);
        }

        return string.Empty;
    }

    /// <summary>
    /// Parsing a file to xml structure
    /// </summary>
    private static XDocument? ConvertToXml(string fileName)
    {

        if(string.IsNullOrEmpty(fileName))
        {
            Logger.LogError(typeof(CfgParser), "No valid directory or missing EIO.cfg file");
            return null;
        }

        var xDoc = new XDocument();
        try
        {

            if (!File.Exists(fileName))
            {
                Logger.LogError(typeof(CfgParser), $"File \"{fileName}\" not found!");
                return null;
            }

            //Read file and split into sections
            var sections = SplitInSections(fileName);

            if (sections is null)
            {
                return xDoc;
            }

            //Parse all sections and create a xml structure
            foreach (Match section in sections)
            {
                if (xDoc.Root is null)
                {
                    var header = GetRootElement(section);
                    if (header is null) continue;
                    xDoc.Add(header);
                    var fi = new FileInfo(fileName);
                    xDoc.Root?.Add(new XAttribute("FileName", fi.FullName));
                }
                else
                {
                    xDoc.Root.Add(ParseSection(section));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(typeof(CfgParser), ex.Message);
        }
        return xDoc;
    }
        
    public static IEnumerable<IoDevice> GetAllDevices(this XDocument eioXml)
    {
        var devices = new List<IoDevice>();
        foreach (var device in DeviceTypes)
        {
            try
            {
                // get all devices
                var dev = (from node in eioXml.Root?.Descendants(device) 
                    from item in node.Descendants("Item")
                    select new IoDevice(item)).ToList();
                // host-devices only!
                dev = (from item in dev 
                    where item.IsHostDevice
                    select item).ToList();
                devices = devices.Concat(dev).ToList();
            }
            catch (Exception e)
            {                    
                Logger.LogError(typeof(CfgParser), e.Message);
            }
        }
        return devices.ToArray();
    }

    /// <summary>
    /// Get header of a section (XElement) and parse the parameter / value
    /// <br/>e.g. EIO_SIGNAL:
    /// <br/>   -Name "doTorqueSupROB_R" -SignalType "DO"
    /// </summary>
    private static XElement? ParseSection(Capture section)
    {
        const string patternHeader = @"^(?<HEADER>\w+)(?=:)";
        var matchHeader = Regex.Match(section.Value, patternHeader);

        if (matchHeader.Success)
        {
            var xElemSection = new XElement(matchHeader.Value);
            foreach (var item in GetValues(section))
            {
                xElemSection.Add(item);
            }
            return xElemSection;
        }
        Logger.LogError(typeof(CfgParser), $"Parsing of section '{matchHeader.Value}' failed");
        return null;
    }

    /// <summary>
    /// Get a list of XElement with parameter values of a section
    /// </summary>
    /// <param name="section"></param>
    /// <returns></returns>
    private static List<XElement> GetValues(Capture section)
    {
        var values = new List<XElement>();
            
        // e.g. '-Name "diCollTorqueSupROB_R"' or '-sampleText 0815'           
        // Differentiate whether a value follows first (only -NAME) and whether the value is in "..." or not (numerical value).
        // The quotation marks must not end up in the result.
        const string pattern = "(?<NAME>(?!-)\\w+)(?(?=\\Z)\\Z|\\s+(?(?=\\s|\\Z)(\\s|\\Z)|(?(?=-?\\d)(?<VALUE>-?\\d+([\\.,]\\d+(E[+-]\\d+)?)?)|\"(?<VALUE>.+?)\")))";

        foreach (var dataSet in GetDataSets(section))
        {
            var xValue = new XElement("Item");
            var matches = Regex.Matches(dataSet, pattern);
            foreach (Match m in matches)
            {
                xValue.Add(new XAttribute(m.Groups["NAME"].Value, m.Groups["VALUE"].Value.Trim("\"".ToCharArray())));
            }

            values.Add(xValue);
        }
        return values;
    }

    /// <summary>
    /// Group all lines of a section to a dataset.<br/>
    /// Multiline datasets with a '\'  will be combined
    /// </summary>
    private static List<string> GetDataSets(Capture section)
    {
        var sep = new[] { "\n", "\r\n" };
        var rawLines = section.Value.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        var dataSets = new List<string>();
        var sbTmpLines = new StringBuilder(rawLines.Length);
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (line.EndsWith('\\'))
            {
                sbTmpLines.Append(line.TrimEnd("\\".ToCharArray()).Trim());
                sbTmpLines.Append(' ');
            }
            else
            {
                if (!line.StartsWith('-')) continue;
                if (sbTmpLines.Length > 0)
                {
                    sbTmpLines.Append(line);
                    dataSets.Add(sbTmpLines.ToString());
                    sbTmpLines.Clear();
                }
                else
                {
                    dataSets.Add(line);
                }
            }
        }

        return dataSets;
    }

    /// <summary>
    /// Get header from the cfg-file
    /// </summary>
    private static XElement? GetRootElement(Capture section)
    {
        //EIO:CFG_1.0:6:1::
        //# 
        const string pattern = @"^(?<NAME>\w+):.*?:(?<MAJOR>\d+):(?<MINOR>\d+)::";

        var match = Regex.Match(section.Value, pattern);
        if (match.Success)
        {
            return new XElement(match.Groups["NAME"].Value, 
                new XAttribute("Version", $"{match.Groups["MAJOR"].Value}.{match.Groups["MINOR"].Value}"));
        }
        return null;
    }

    /// <summary>
    /// Divides the cfg into sections
    /// <br/>A section is defined by:
    /// <br/>SECTION_NAME1:
    /// <br/>    -Name "sampleName1" -SignalType "DI"
    /// <br/>    -Name "sampleName2" -SignalType "DO"
    /// <br/>#
    /// <br/>SECTION_NAME2:
    /// <br/> . . . 
    /// </summary>
    private static MatchCollection? SplitInSections(string fileName)
    {
        try
        {
            var rawText = File.ReadAllText(fileName, Encoding.UTF8);
            const string begin = @"^\w+:";
            const string end = "(#(\\s+)?\r\n|\\Z)";
            const string pattern = $"{begin}(?>{begin}(?<DEPTH>)|{end}(?<-DEPTH>)|(?!({begin}|{end}))(\\w+|\\W))*{end}(?(DEPTH)(?!))";
            return Regex.Matches(rawText, pattern, RegexOptions.Multiline);

        }
        catch (Exception e)
        {
            Logger.LogError(typeof(CfgParser), e.Message);
            return null;
        }
    }
}