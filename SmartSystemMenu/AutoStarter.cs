using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace SmartSystemMenu
{
    static class AutoStarter
    {
        private const string RUN_LOCATION = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const int SCHEDULER_TIMEOUT_MS = 30000;
        private static readonly SecurityIdentifier[] LowPrivilegeSids =
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            new SecurityIdentifier(WellKnownSidType.WorldSid, null)
        };
        private static readonly FileSystemRights WriteLikeRights =
            FileSystemRights.WriteData |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories |
            FileSystemRights.Write |
            FileSystemRights.Modify |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership |
            FileSystemRights.FullControl;

        public static bool TryEnableAutoStart(string keyName, string assemblyLocation, bool configureScheduler, out string reason)
        {
            if (!TryValidateTrustedInstallPath(assemblyLocation, out var trustedAssemblyLocation, out reason))
            {
                return false;
            }

            if (!TrySetAutoStartByRegister(keyName, trustedAssemblyLocation, out reason))
            {
                return false;
            }

            if (configureScheduler && !TrySetAutoStartByScheduler(keyName, trustedAssemblyLocation, out reason))
            {
                TryUnsetAutoStartByRegister(keyName, out _);
                return false;
            }

            reason = null;
            return true;
        }

        public static bool TryDisableAutoStart(string keyName, bool removeScheduler, out string reason)
        {
            var errors = new List<string>();
            if (!TryUnsetAutoStartByRegister(keyName, out var registerError))
            {
                errors.Add(registerError);
            }

            if (removeScheduler && !TryUnsetAutoStartByScheduler(keyName, out var schedulerError))
            {
                errors.Add(schedulerError);
            }

            reason = errors.Any() ? string.Join(Environment.NewLine, errors) : null;
            return !errors.Any();
        }

        private static bool TrySetAutoStartByRegister(string keyName, string assemblyLocation, out string reason)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RUN_LOCATION);
                if (key == null)
                {
                    reason = "Unable to open HKCU autostart registry key.";
                    return false;
                }

                key.SetValue(keyName, assemblyLocation);
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Failed to enable registry autostart: {ex.Message}";
                return false;
            }
        }

        private static bool TryUnsetAutoStartByRegister(string keyName, out string reason)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RUN_LOCATION);
                if (key == null)
                {
                    reason = "Unable to open HKCU autostart registry key.";
                    return false;
                }

                key.DeleteValue(keyName, false);
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Failed to disable registry autostart: {ex.Message}";
                return false;
            }
        }

        private static bool TrySetAutoStartByScheduler(string keyName, string assemblyLocation, out string reason)
        {
            var schedulerPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            if (!File.Exists(schedulerPath))
            {
                reason = $"Task Scheduler executable was not found: {schedulerPath}";
                return false;
            }

            var arguments = string.Format("/create /sc onlogon /tn \"{0}\" /rl highest /f /tr \"\\\"{1}\\\"\"", keyName, assemblyLocation);
            return TryRunSchedulerCommand(schedulerPath, arguments, out reason);
        }

        private static bool TryUnsetAutoStartByScheduler(string keyName, out string reason)
        {
            var schedulerPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            if (!File.Exists(schedulerPath))
            {
                reason = $"Task Scheduler executable was not found: {schedulerPath}";
                return false;
            }

            var arguments = string.Format("/delete /tn \"{0}\" /f", keyName);
            if (TryRunSchedulerCommand(schedulerPath, arguments, out reason))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(reason) &&
                (reason.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 reason.IndexOf("cannot find the file specified", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                reason = null;
                return true;
            }

            return false;
        }

        private static bool TryRunSchedulerCommand(string fileName, string arguments, out string reason)
        {
            using var scheduleProcess = new Process();
            scheduleProcess.StartInfo.CreateNoWindow = true;
            scheduleProcess.StartInfo.UseShellExecute = false;
            scheduleProcess.StartInfo.RedirectStandardOutput = true;
            scheduleProcess.StartInfo.RedirectStandardError = true;
            scheduleProcess.StartInfo.FileName = fileName;
            scheduleProcess.StartInfo.Arguments = arguments;

            try
            {
                if (!scheduleProcess.Start())
                {
                    reason = "Task Scheduler command could not be started.";
                    return false;
                }

                if (!scheduleProcess.WaitForExit(SCHEDULER_TIMEOUT_MS))
                {
                    scheduleProcess.Kill();
                    reason = "Task Scheduler command timed out.";
                    return false;
                }

                var output = scheduleProcess.StandardOutput.ReadToEnd();
                var error = scheduleProcess.StandardError.ReadToEnd();
                if (scheduleProcess.ExitCode != 0)
                {
                    reason = $"Task Scheduler command failed (exit code {scheduleProcess.ExitCode}). {FormatCommandOutput(output, error)}";
                    return false;
                }

                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Task Scheduler command failed: {ex.Message}";
                return false;
            }
        }

        private static string FormatCommandOutput(string output, string error)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(output))
            {
                builder.Append(output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(error.Trim());
            }

            return builder.Length > 0 ? builder.ToString() : "No additional details were reported.";
        }

        private static bool TryValidateTrustedInstallPath(string assemblyLocation, out string trustedAssemblyLocation, out string reason)
        {
            trustedAssemblyLocation = null;
            if (!SystemUtils.TryResolveExecutablePath(assemblyLocation, out var executablePath, out var pathError))
            {
                reason = $"Autostart is blocked: {pathError}";
                return false;
            }

            if (IsPathInUntrustedLocation(executablePath))
            {
                reason = $"Autostart is blocked because the application path is not trusted: {executablePath}";
                return false;
            }

            var directoryPath = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                reason = $"Autostart is blocked because the install directory is invalid: {directoryPath}";
                return false;
            }

            if (!TryEnsurePathIsNotWritableByLowPrivilegeUsers(executablePath, false, out reason))
            {
                return false;
            }

            if (!TryEnsurePathIsNotWritableByLowPrivilegeUsers(directoryPath, true, out reason))
            {
                return false;
            }

            trustedAssemblyLocation = executablePath;
            reason = null;
            return true;
        }

        private static bool IsPathInUntrustedLocation(string executablePath)
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsPathUnderRoot(executablePath, userProfilePath))
            {
                return true;
            }

            var tempPath = Path.GetTempPath();
            return IsPathUnderRoot(executablePath, tempPath);
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullRootPath = Path.GetFullPath(rootPath);
                if (!fullRootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    fullRootPath += Path.DirectorySeparatorChar;
                }

                return fullPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryEnsurePathIsNotWritableByLowPrivilegeUsers(string path, bool isDirectory, out string reason)
        {
            try
            {
                AuthorizationRuleCollection accessRules = isDirectory
                    ? Directory.GetAccessControl(path).GetAccessRules(true, true, typeof(SecurityIdentifier))
                    : File.GetAccessControl(path).GetAccessRules(true, true, typeof(SecurityIdentifier));

                foreach (FileSystemAccessRule accessRule in accessRules)
                {
                    if (accessRule.AccessControlType != AccessControlType.Allow)
                    {
                        continue;
                    }

                    if (!(accessRule.IdentityReference is SecurityIdentifier sid))
                    {
                        continue;
                    }

                    if (!LowPrivilegeSids.Any(lowPrivilegeSid => lowPrivilegeSid.Equals(sid)))
                    {
                        continue;
                    }

                    if ((accessRule.FileSystemRights & WriteLikeRights) != 0)
                    {
                        reason = $"Autostart is blocked because '{path}' grants write-like permissions to low-privilege identity '{sid.Value}'.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                reason = $"Autostart is blocked because ACL validation failed for '{path}': {ex.Message}";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool IsAutoStartByRegisterEnabled(string keyName, string assemblyLocation)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION);
            if (key == null) return false;
            var value = (string)key.GetValue(keyName);
            if (string.IsNullOrEmpty(value)) return false;
            var result = string.Equals(value, assemblyLocation, StringComparison.OrdinalIgnoreCase);
            return result;
        }
    }
}
