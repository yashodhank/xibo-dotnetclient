/*
 * Xibo - Digitial Signage - http://www.xibo.org.uk
 * Copyright (C) 2012-16 Daniel Garner
 *
 * This file is part of Xibo.
 *
 * Xibo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * any later version. 
 *
 * Xibo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with Xibo.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using XiboClient.Properties;
using XiboClient.Logic;

namespace XiboClient
{
    class ShellCommand : Media
    {
        string _command = "";
        string _code = "";
        bool _launchThroughCmd = true;
        bool _terminateCommand = false;
        bool _useTaskKill = false;
        int _processId;

        public ShellCommand(RegionOptions options)
            : base(options.width, options.height, options.top, options.left)
        {
            _command = Uri.UnescapeDataString(options.Dictionary.Get("windowsCommand")).Replace('+', ' ');
            _code = options.Dictionary.Get("commandCode");

            // Default to launching through CMS for backwards compatiblity
            _launchThroughCmd = (options.Dictionary.Get("launchThroughCmd", "1") == "1");
            
            // Termination
            _terminateCommand = (options.Dictionary.Get("terminateCommand") == "1");
            _useTaskKill = (options.Dictionary.Get("useTaskkill") == "1");
        }

        public override void RenderMedia()
        {
            if (!string.IsNullOrEmpty(_code))
            {
                // Stored command
                bool success;

                try
                {
                    Command command = Command.GetByCode(_code);
                    success = command.Run();
                }
                catch (Exception e)
                {
                    Trace.WriteLine(new LogMessage("ScheduleManager - Run", "Cannot run Command: " + e.Message), LogType.Error.ToString());
                    success = false;
                }

                // Notify the state of the command (success or failure)
                using (xmds.xmds statusXmds = new xmds.xmds())
                {
                    statusXmds.Url = ApplicationSettings.Default.XiboClient_xmds_xmds;
                    statusXmds.NotifyStatusAsync(ApplicationSettings.Default.ServerKey, ApplicationSettings.Default.HardwareKey, "{\"lastCommandSuccess\":" + success + "}");
                }
            }
            else
            {
                // shell command

                // Is this module enabled?
                if (ApplicationSettings.Default.EnableShellCommands)
                {
                    // Check to see if we have an allow list
                    if (!string.IsNullOrEmpty(ApplicationSettings.Default.ShellCommandAllowList))
                    {
                        // Array of allowed commands
                        string[] allowedCommands = ApplicationSettings.Default.ShellCommandAllowList.Split(',');

                        // Check we are allowed to execute the command
                        bool found = false;

                        foreach (string allowedCommand in allowedCommands)
                        {
                            if (_command.StartsWith(allowedCommand))
                            {
                                found = true;
                                ExecuteShellCommand();
                                break;
                            }
                        }

                        if (!found)
                            Trace.WriteLine(new LogMessage("ShellCommand - RenderMedia", "Shell Commands not in allow list: " + ApplicationSettings.Default.ShellCommandAllowList), LogType.Error.ToString());
                    }
                    else
                    {
                        // All commands are allowed
                        ExecuteShellCommand();
                    }
                }
                else
                {
                    Trace.WriteLine(new LogMessage("ShellCommand - RenderMedia", "Shell Commands are disabled"), LogType.Error.ToString());
                }
            }

            // All shell commands have a duration of 1
            base.RenderMedia();
        }

        /// <summary>
        /// Execute the shell command
        /// </summary>
        private void ExecuteShellCommand()
        {
            Trace.WriteLine(new LogMessage("ShellCommand - ExecuteShellCommand", _command), LogType.Info.ToString());

            // Execute the commend
            if (!string.IsNullOrEmpty(_command))
            {
                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();

                    if (_launchThroughCmd)
                    {
                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        startInfo.FileName = "cmd.exe";
                        startInfo.Arguments = "/C " + _command;
                    }
                    else
                    {
                        // Split the command into a command string and arguments.
                        string[] splitCommand = _command.Split(new[] { ' ' }, 2);
                        startInfo.FileName = splitCommand[0];

                        if (splitCommand.Length > 1)
                            startInfo.Arguments = splitCommand[1];                        
                    }

                    process.StartInfo = startInfo;
                    process.Start();

                    // Grab the ID
                    _processId = process.Id;
                }
            }
        }

        /// <summary>
        /// Terminates the shell command
        /// </summary>
        private void TerminateCommand()
        {
            Trace.WriteLine(new LogMessage("ShellCommand - TerminateCommand", _command), LogType.Info.ToString());

            if (_processId == 0)
            {
                Trace.WriteLine(new LogMessage("ShellCommand - TerminateCommand", "ProcessID empty for command: " + _command), LogType.Error.ToString());
                return;
            }

            if (_useTaskKill)
            {
                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();

                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    startInfo.FileName = "taskkill.exe";
                    startInfo.Arguments = "/pid " + _processId.ToString();

                    process.StartInfo = startInfo;
                    process.Start();
                }
            }
            else
            {
                using (Process process = Process.GetProcessById(_processId))
                {
                    process.Kill();
                }
            }
        }

        /// <summary>
        /// Dispose of this text item
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Remove the webbrowser control
                try
                {
                    // Terminate the command
                    TerminateCommand();
                }
                catch
                {
                    Debug.WriteLine(new LogMessage("Unable to terminate command", "Dispose"));
                }
            }

            base.Dispose(disposing);
        }
    }
}
