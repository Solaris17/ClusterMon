using System;
using System.Collections.Generic;
using System.Diagnostics; // For EventLog manipulation
using System.Linq;
using Microsoft.Win32;
using System.ServiceProcess; // For ServiceController
using System.Management; // For WMI
using System.Runtime.InteropServices;
using WUApiLib;  // For Windows Update Agent API
using System.IO;

namespace ClusterMonitor
{
    class Program
    {
        // Constants for event log and registry paths
        private const string EventLogName = "ClusterMonitor";
        private const string EventSourceName = "ClusterMonitorScript";
        private const string RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private const string RegistryKey = "NoAutoRebootWithLoggedOnUsers";

        static void Main(string[] args)
        {
            // Check for dry run flag
            bool dryRun = args.Contains("--dry-run");

            // Initialize the custom event log
            InitializeEventLog();

            // Ensure WMI classes for clustering are available
            EnsureWmiClasses();

            // Get the list of cluster nodes dynamically
            List<string> nodes = GetClusterNodes();

            // Log the retrieved nodes
            WriteEventLog($"Retrieved {nodes.Count} cluster nodes.", EventLogEntryType.Information);

            // Count the number of nodes that are online
            int onlineNodes = nodes.Count(IsNodeOnline);

            // Log the number of online nodes
            WriteEventLog($"{onlineNodes} nodes are online.", EventLogEntryType.Information);

            // If fewer than three nodes are online, prevent automatic reboots
            if (onlineNodes < 3)
            {
                WriteEventLog("Fewer than three nodes are online. Preventing automatic reboots.", EventLogEntryType.Warning);

                if (!dryRun)
                {
                    // Set registry key to prevent automatic reboots
                    SetRegistryKeyValue(1);
                    // Stop the Windows Update service
                    StopService("wuauserv");
                }
                else
                {
                    WriteEventLog("Dry run: Registry key set to prevent reboots and Windows Update service stopped.", EventLogEntryType.Information);
                }
            }
            else // If three or more nodes are online, allow automatic reboots
            {
                WriteEventLog("Three or more nodes are online. Allowing automatic reboots if needed.", EventLogEntryType.Information);

                if (!dryRun)
                {
                    // Set registry key to allow automatic reboots
                    SetRegistryKeyValue(0);
                    // Start the Windows Update service
                    StartService("wuauserv");
                    // Force Windows Update detection
                    ForceUpdateDetection();
                }
                else
                {
                    WriteEventLog("Dry run: Registry key set to allow reboots and Windows Update service started. Update detection would be forced.", EventLogEntryType.Information);
                }
            }
        }

        // Ensure WMI classes for clustering are available
        private static void EnsureWmiClasses()
        {
            try
            {
                // Check if the MSCluster_Node class exists
                using (var searcher = new ManagementObjectSearcher("root\\MSCluster", "SELECT * FROM MSCluster_Node"))
                {
                    searcher.Get();
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
            {
                // Compile the ClusWMI.mof file if the class is not found
                WriteEventLog("MSCluster_Node class not found. Compiling ClusWMI.mof...", EventLogEntryType.Warning);
                try
                {
                    Process process = new Process();
                    process.StartInfo.FileName = "mofcomp";
                    process.StartInfo.Arguments = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wbem", "ClusWMI.mof");
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        WriteEventLog("Successfully compiled ClusWMI.mof.", EventLogEntryType.Information);
                    }
                    else
                    {
                        WriteEventLog("Failed to compile ClusWMI.mof.", EventLogEntryType.Error);
                    }
                }
                catch (Exception e)
                {
                    WriteEventLog($"Error compiling ClusWMI.mof: {e.Message}", EventLogEntryType.Error);
                }
            }
            catch (Exception ex)
            {
                WriteEventLog($"Unexpected error while ensuring WMI classes: {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Function to retrieve the list of cluster nodes using WMI
        private static List<string> GetClusterNodes()
        {
            List<string> nodes = new List<string>();

            try
            {
                // Query the MSCluster_Node class to get all cluster nodes
                using (var searcher = new ManagementObjectSearcher("root\\MSCluster", "SELECT * FROM MSCluster_Node"))
                {
                    var nodeCollection = searcher.Get();
                    foreach (ManagementObject node in nodeCollection)
                    {
                        nodes.Add(node["Name"].ToString());
                    }
                }
            }
            catch (ManagementException ex)
            {
                // Log WMI-specific errors with detailed message
                WriteEventLog($"Error retrieving cluster nodes: {ex.Message}. Please ensure the Failover Clustering feature is installed and the Cluster Service is running. WMI Error Code: {ex.ErrorCode}", EventLogEntryType.Error);
                Console.WriteLine($"WMI Error: {ex.ErrorCode}, Message: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Log any other errors encountered during the node retrieval
                WriteEventLog($"Unexpected error retrieving cluster nodes: {ex.Message}", EventLogEntryType.Error);
                Console.WriteLine($"Unexpected Error: {ex.Message}");
            }

            return nodes;
        }

        // Function to check if a specific node is online
        private static bool IsNodeOnline(string nodeName)
        {
            try
            {
                // Query the MSCluster_Node class for the specific node's state
                using (var searcher = new ManagementObjectSearcher("root\\MSCluster", $"SELECT State FROM MSCluster_Node WHERE Name='{nodeName}'"))
                {
                    var nodes = searcher.Get();
                    foreach (ManagementObject node in nodes)
                    {
                        // Check the state of the node (0 indicates the node is up)
                        if (node["State"] != null && (uint)node["State"] == 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (ManagementException ex)
            {
                // Log WMI-specific errors with detailed message
                WriteEventLog($"Error checking node status for '{nodeName}': {ex.Message}", EventLogEntryType.Error);
                Console.WriteLine($"WMI Error: {ex.ErrorCode}, Message: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                // Log any other errors encountered during the node state check
                WriteEventLog($"Unexpected error checking node status for '{nodeName}': {ex.Message}", EventLogEntryType.Error);
                Console.WriteLine($"Unexpected Error: {ex.Message}");
                return false;
            }

            return false;
        }

        // Function to set the registry key value to control automatic reboots
        private static void SetRegistryKeyValue(int value)
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(RegistryPath))
                {
                    key.SetValue(RegistryKey, value, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                // Log any errors encountered during the registry key set
                WriteEventLog($"Error setting registry key value: {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Function to stop a specified service
        private static void StopService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any errors encountered during service stop
                WriteEventLog($"Error stopping service '{serviceName}': {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Function to start a specified service
        private static void StartService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any errors encountered during service start
                WriteEventLog($"Error starting service '{serviceName}': {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Function to force Windows Update detection
        private static void ForceUpdateDetection()
        {
            try
            {
                // Initialize the Windows Update session and searcher
                UpdateSession updateSession = new UpdateSession();
                IUpdateSearcher updateSearcher = updateSession.CreateUpdateSearcher();
                WriteEventLog("Starting update detection...", EventLogEntryType.Information);

                // Search for updates that are not installed
                ISearchResult searchResult = updateSearcher.Search("IsInstalled=0");
                if (searchResult.Updates.Count > 0)
                {
                    WriteEventLog($"{searchResult.Updates.Count} pending updates detected.", EventLogEntryType.Information);
                }
                else
                {
                    WriteEventLog("No pending updates detected.", EventLogEntryType.Information);
                }
            }
            catch (COMException ex)
            {
                // Log any errors encountered during update detection
                WriteEventLog($"Update detection failed: {ex.Message}", EventLogEntryType.Error);
            }
        }

        // Function to initialize the custom event log
        private static void InitializeEventLog()
        {
            try
            {
                // Check if the event log source exists, if not create it
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, EventLogName);
                }
            }
            catch (Exception ex)
            {
                // Log any errors encountered during event log initialization
                Console.WriteLine($"Error initializing event log: {ex.Message}");
            }
        }

        // Function to write a message to the custom event log
        private static void WriteEventLog(string message, EventLogEntryType eventType)
        {
            try
            {
                // Write the log entry to the event log
                EventLog.WriteEntry(EventSourceName, message, eventType);
            }
            catch (Exception ex)
            {
                // Log any errors encountered during event log writing
                Console.WriteLine($"Error writing to event log: {ex.Message}");
            }
        }
    }
}
