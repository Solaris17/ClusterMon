## Stats

<p align="center">
<a href="https://github.com/Solaris17/ClusterMon/releases"><img alt="GitHub release (with filter)" src="https://img.shields.io/github/v/release/Solaris17/ClusterMon"></a>
<a href="https://github.com/Solaris17/ClusterMon/releases"><img alt="GitHub all releases" src="https://img.shields.io/github/downloads/Solaris17/ClusterMon/total?label=Downloads"></a>
</p>

# ClusterMonitor

ClusterMonitor is a .NET application designed to manage automatic reboots in a Windows Server cluster environment. It ensures that critical updates and reboots do not occur when fewer than three cluster nodes are online, thereby maintaining the availability and stability of your cluster and increasing the chances of quorum rebuild.

## Features

- Dynamic Cluster Node Detection: Automatically detects and lists all nodes in the cluster.
- Node Status Check: Verifies the online status of each node.
- Automatic Reboot Management: Prevents automatic reboots if fewer than three nodes are online.
- Windows Update Service Control: Starts or stops the Windows Update service based on the cluster status.
- Dry Run Mode: Allows testing the application without making any changes to the system.
- Automatic WMI Class Compilation: Ensures the necessary WMI classes for clustering are available by compiling ClusWMI.mof if needed.

## Requirements

- .NET 6.0 or higher
- Administrative privileges
- Failover Clustering feature installed and Cluster Service running

## Installation

- Clone the Repository:

git clone https://github.com/Solaris17/ClusterMon.git

- Open the Project:

Open the project in Visual Studio.

Restore NuGet Packages:
In Visual Studio, right-click on the solution and select "Restore NuGet Packages".

- Build the Solution:

Build the solution to generate the executable.

Publish the Application (Optional):

```dotnet publish -c Release -r win-x64 --self-contained true```

## Usage

Run the application with the necessary privileges to ensure it can access WMI and modify system settings.
The most basic way is to create a scheduled task that runs the application with the highest privileges and enabling "Run whether user is logged on or not".

To test:

ClusterMonitor.exe [--dry-run]

## Parameters

```--dry-run: Runs the script without making any changes, useful for testing.```

## Functionality Overview

- Initialize Event Log: Creates a custom event log for recording script actions and errors.
- Ensure WMI Classes for Clustering: Compiles the ClusWMI.mof file if the necessary WMI classes are not available.
- Retrieve Cluster Nodes: Uses WMI to dynamically list all nodes in the cluster.
- Check Node Status: Verifies if each node is online.
- Manage Automatic Reboots: 
  - If fewer than three nodes are online:
    - Prevents automatic reboots by setting a specific registry key.
    - Stops the Windows Update service.
  - If three or more nodes are online:
    - Allows automatic reboots by resetting the registry key.
    - Starts the Windows Update service and forces an update detection.

## Example
Dry Run
To test the script without making any changes:

```ClusterMonitor.exe --dry-run```

Production Run
To execute the script with changes:

```ClusterMonitor.exe```

## Logging

Logs are written to the custom event log named "ClusterMonitor".
Use Event Viewer to review the logs under "Applications and Services Logs".
