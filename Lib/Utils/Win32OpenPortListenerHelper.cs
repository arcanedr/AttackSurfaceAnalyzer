﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;

namespace AttackSurfaceAnalyzer.Utils
{
    /// <summary>
    /// Static class that returns the list of processes and the ports those processes use.
    /// </summary>
    public static class Win32ProcessPorts
    {
        private static List<ProcessPort> CachedProcessPortMap;

        /// <summary>
        /// A list of ProcesesPorts that contain the mapping of processes and the ports that the process uses.
        /// </summary>
        public static List<ProcessPort> ProcessPortMap
        {
            get
            {
                if (CachedProcessPortMap == null)
                {
                    CachedProcessPortMap = GetNetStatPorts();
                }
                return CachedProcessPortMap;
            }
        }


        /// <summary>
        /// This method distills the output from netstat -a -n -o into a list of ProcessPorts that provide a mapping between
        /// the process (name and id) and the ports that the process is using.
        /// </summary>
        /// <returns></returns>
        private static List<ProcessPort> GetNetStatPorts()
        {
            List<ProcessPort> ProcessPorts = new List<ProcessPort>();

            try
            {
                using (Process Proc = new Process())
                {

                    ProcessStartInfo StartInfo = new ProcessStartInfo()
                    {
                        FileName = "netstat.exe",
                        Arguments = "-a -n -o",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    Proc.StartInfo = StartInfo;
                    Proc.Start();

                    StreamReader StandardOutput = Proc.StandardOutput;
                    StreamReader StandardError = Proc.StandardError;

                    string NetStatContent = StandardOutput.ReadToEnd() + StandardError.ReadToEnd();

                    if (Proc.ExitCode != 0)
                    {
                        Log.Error("Unable to run netstat.exe. Open ports will not be available.");
                        return ProcessPorts;
                    }

                    string[] NetStatRows = Regex.Split(NetStatContent, "\r\n");

                    foreach (var _outputLine in NetStatRows)
                    {
                        if (_outputLine == null)
                        {
                            continue;
                        }

                        var outputLine = _outputLine.Trim();

                        string[] Tokens = Regex.Split(outputLine, @"\s+");
                        try
                        {
                            if (Tokens.Length < 4)
                            {
                                continue;
                            }
                            string IpAddress = Regex.Replace(Tokens[1], @"\[(.*?)\]", "1.1.1.1");

                            if (Tokens.Length > 4 &&  Tokens[0].Equals("TCP"))
                            {
                                if(!Tokens[3].Equals("LISTENING")) { continue; }
                                ProcessPorts.Add(new ProcessPort(
                                    GetProcessName(Convert.ToInt32(Tokens[4])),
                                    Convert.ToInt32(Tokens[4]),
                                    IpAddress.Contains("1.1.1.1") ? String.Format("{0}v6", Tokens[1]) : String.Format("{0}v4", Tokens[1]),
                                    Convert.ToInt32(IpAddress.Split(':')[1])
                                ));

                            }
                            else if (Tokens.Length == 4 && (Tokens[0].Equals("UDP")))
                            {
                                ProcessPorts.Add(new ProcessPort(
                                    GetProcessName(Convert.ToInt32(Tokens[3])),
                                    Convert.ToInt32(Tokens[3]),
                                    IpAddress.Contains("1.1.1.1") ? String.Format("{0}v6", Tokens[1]) : String.Format("{0}v4", Tokens[1]),
                                    Convert.ToInt32(IpAddress.Split(':')[1])
                                ));
                            }
                            else
                            {
                                if (!outputLine.StartsWith("Proto") && !outputLine.StartsWith("Active") && !String.IsNullOrWhiteSpace(outputLine))
                                {
                                    Log.Warning("Primary Parsing error when processing netstat.exe output: {0}", outputLine);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Warning("Secondary Parsing error when processing netstat.exe output: {0}", outputLine);
                            Log.Warning(e.Message);
                            Log.Warning(e.GetType().ToString());
                            Telemetry.TrackTrace(Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Error, e);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Error processing open ports: {0}", ex.Message);
            }
            return ProcessPorts;
        }

        /// <summary>
        /// Private method that handles pulling the process name (if one exists) from the process id.
        /// </summary>
        /// <param name="ProcessId"></param>
        /// <returns></returns>
        private static string GetProcessName(int ProcessId)
        {
            try
            {
                return Process.GetProcessById(ProcessId).MainModule.FileName;
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// A mapping for processes to ports and ports to processes that are being used in the system.
    /// </summary>
    public class ProcessPort
    {
        private string _ProcessName = String.Empty;
        private int _ProcessId = 0;
        private string _Protocol = String.Empty;
        private int _PortNumber = 0;

        /// <summary>
        /// Internal constructor to initialize the mapping of process to port.
        /// </summary>
        /// <param name="ProcessName">Name of process to be </param>
        /// <param name="ProcessId"></param>
        /// <param name="Protocol"></param>
        /// <param name="PortNumber"></param>
        internal ProcessPort(string ProcessName, int ProcessId, string Protocol, int PortNumber)
        {
            _ProcessName = ProcessName;
            _ProcessId = ProcessId;
            _Protocol = Protocol;
            _PortNumber = PortNumber;
        }

        public string ProcessPortDescription
        {
            get
            {
                return String.Format("{0} ({1} port {2} pid {3})", _ProcessName, _Protocol, _PortNumber, _ProcessId);
            }
        }
        public string ProcessName
        {
            get { return _ProcessName; }
        }
        public int ProcessId
        {
            get { return _ProcessId; }
        }
        public string Protocol
        {
            get { return _Protocol; }
        }
        public int PortNumber
        {
            get { return _PortNumber; }
        }
    }
}