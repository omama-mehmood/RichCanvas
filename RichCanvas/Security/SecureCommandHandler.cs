using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace RichCanvas.Security
{
    /// <summary>
    /// Provides secure command execution with validation and sanitization
    /// to prevent command injection and other security vulnerabilities.
    /// </summary>
    public static class SecureCommandHandler
    {
        private static readonly HashSet<Type> AllowedCommandTypes = new HashSet<Type>
        {
            typeof(RoutedUICommand),
            typeof(RoutedCommand),
            typeof(ApplicationCommands),
            typeof(NavigationCommands),
            typeof(MediaCommands),
            typeof(ComponentCommands),
            typeof(SystemCommands)
        };

        private static readonly Dictionary<string, DateTime> CommandThrottling = new Dictionary<string, DateTime>();
        private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(50);
        
        // Regex for validating safe command parameters
        private static readonly Regex SafeParameterPattern = new Regex(@"^[a-zA-Z0-9._\-\s]{1,256}$", RegexOptions.Compiled);
        
        // Maximum allowed parameter size to prevent memory attacks
        private const int MaxParameterSize = 1024;

        /// <summary>
        /// Validates and executes a command with security checks.
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="parameter">The command parameter</param>
        /// <param name="target">The target element</param>
        /// <returns>True if the command was executed successfully</returns>
        public static bool ExecuteSecureCommand(ICommand command, object parameter, IInputElement target)
        {
            try
            {
                // 1. Validate command is not null
                if (command == null)
                {
                    LogSecurityEvent("Attempted to execute null command");
                    return false;
                }

                // 2. Validate command type is allowed
                if (!IsCommandTypeAllowed(command))
                {
                    LogSecurityEvent($"Blocked execution of unauthorized command type: {command.GetType().FullName}");
                    return false;
                }

                // 3. Validate parameter
                if (!IsParameterSafe(parameter))
                {
                    LogSecurityEvent($"Blocked command execution with unsafe parameter: {parameter?.GetType().Name}");
                    return false;
                }

                // 4. Check command throttling
                if (!CheckThrottling(command))
                {
                    LogSecurityEvent($"Command throttled: {GetCommandIdentifier(command)}");
                    return false;
                }

                // 5. Validate target
                if (target == null || !IsTargetSafe(target))
                {
                    LogSecurityEvent("Invalid or unsafe target for command execution");
                    return false;
                }

                // 6. Check if command can execute
                if (!command.CanExecute(parameter))
                {
                    return false;
                }

                // 7. Execute command in a safe context
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        command.Execute(parameter);
                    }
                    catch (SecurityException se)
                    {
                        LogSecurityEvent($"Security exception during command execution: {se.Message}");
                        throw;
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                LogSecurityEvent($"Exception during secure command execution: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates that a command type is in the allowed list.
        /// </summary>
        private static bool IsCommandTypeAllowed(ICommand command)
        {
            var commandType = command.GetType();
            
            // Check if it's a known safe command type
            if (AllowedCommandTypes.Contains(commandType))
                return true;

            // Check if it's derived from a safe type
            foreach (var allowedType in AllowedCommandTypes)
            {
                if (allowedType.IsAssignableFrom(commandType))
                    return true;
            }

            // Special case for RichCanvasCommands
            if (commandType.Namespace == "RichCanvas" && commandType.Name == "RichCanvasCommands")
                return true;

            return false;
        }

        /// <summary>
        /// Validates that a parameter is safe to use.
        /// </summary>
        private static bool IsParameterSafe(object parameter)
        {
            if (parameter == null)
                return true;

            // Check parameter size
            if (parameter is string str)
            {
                if (str.Length > MaxParameterSize)
                    return false;

                // Check for potential injection patterns
                if (ContainsInjectionPattern(str))
                    return false;

                // Validate against safe pattern
                return SafeParameterPattern.IsMatch(str);
            }

            // Check for primitive types (generally safe)
            var type = parameter.GetType();
            if (type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid))
                return true;

            // Check for Point/Size/Rect structures (common in WPF)
            if (type == typeof(Point) || type == typeof(Size) || type == typeof(Rect) || type == typeof(Vector))
            {
                return ValidateNumericStructure(parameter);
            }

            // Block complex objects by default (safe-list approach)
            return false;
        }

        /// <summary>
        /// Checks for common injection patterns in strings.
        /// </summary>
        private static bool ContainsInjectionPattern(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Common injection patterns to block
            string[] dangerousPatterns = 
            {
                "<script", "javascript:", "onclick", "onerror", "onload",
                "exec(", "eval(", "expression(", "vbscript:", "file://",
                "..\\", "../", "cmd.exe", "powershell", "/bin/", "\0",
                "drop ", "delete ", "insert ", "update ", "--", "/*", "*/",
                "xp_", "sp_", "0x"
            };

            var lowerValue = value.ToLowerInvariant();
            foreach (var pattern in dangerousPatterns)
            {
                if (lowerValue.Contains(pattern))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Validates numeric structures for safe values.
        /// </summary>
        private static bool ValidateNumericStructure(object value)
        {
            if (value is Point point)
            {
                return !double.IsNaN(point.X) && !double.IsNaN(point.Y) &&
                       !double.IsInfinity(point.X) && !double.IsInfinity(point.Y);
            }

            if (value is Size size)
            {
                return !double.IsNaN(size.Width) && !double.IsNaN(size.Height) &&
                       !double.IsInfinity(size.Width) && !double.IsInfinity(size.Height) &&
                       size.Width >= 0 && size.Height >= 0;
            }

            if (value is Rect rect)
            {
                return !double.IsNaN(rect.X) && !double.IsNaN(rect.Y) &&
                       !double.IsNaN(rect.Width) && !double.IsNaN(rect.Height) &&
                       !double.IsInfinity(rect.X) && !double.IsInfinity(rect.Y) &&
                       !double.IsInfinity(rect.Width) && !double.IsInfinity(rect.Height) &&
                       rect.Width >= 0 && rect.Height >= 0;
            }

            if (value is Vector vector)
            {
                return !double.IsNaN(vector.X) && !double.IsNaN(vector.Y) &&
                       !double.IsInfinity(vector.X) && !double.IsInfinity(vector.Y);
            }

            return true;
        }

        /// <summary>
        /// Validates that the target element is safe.
        /// </summary>
        private static bool IsTargetSafe(IInputElement target)
        {
            // Ensure target is a UIElement or ContentElement
            return target is UIElement || target is ContentElement;
        }

        /// <summary>
        /// Implements command throttling to prevent rapid-fire execution.
        /// </summary>
        private static bool CheckThrottling(ICommand command)
        {
            var commandId = GetCommandIdentifier(command);
            
            lock (CommandThrottling)
            {
                if (CommandThrottling.TryGetValue(commandId, out DateTime lastExecution))
                {
                    if (DateTime.UtcNow - lastExecution < ThrottleInterval)
                    {
                        return false; // Command throttled
                    }
                }

                CommandThrottling[commandId] = DateTime.UtcNow;
                
                // Clean up old entries
                if (CommandThrottling.Count > 100)
                {
                    var cutoffTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
                    var keysToRemove = new List<string>();
                    
                    foreach (var kvp in CommandThrottling)
                    {
                        if (kvp.Value < cutoffTime)
                            keysToRemove.Add(kvp.Key);
                    }
                    
                    foreach (var key in keysToRemove)
                        CommandThrottling.Remove(key);
                }

                return true;
            }
        }

        /// <summary>
        /// Gets a unique identifier for a command.
        /// </summary>
        private static string GetCommandIdentifier(ICommand command)
        {
            if (command is RoutedCommand routedCommand)
            {
                return $"{routedCommand.OwnerType?.FullName}:{routedCommand.Name}";
            }
            
            return command.GetType().FullName ?? "UnknownCommand";
        }

        /// <summary>
        /// Logs security-related events.
        /// </summary>
        private static void LogSecurityEvent(string message)
        {
            // In production, this should log to a security audit log
            Debug.WriteLine($"[SECURITY] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} - {message}");
            
            // Optional: Send to Windows Event Log
            try
            {
                if (EventLog.SourceExists("RichCanvas"))
                {
                    EventLog.WriteEntry("RichCanvas", message, EventLogEntryType.Warning);
                }
            }
            catch
            {
                // Silently fail if event log is not available
            }
        }

        /// <summary>
        /// Registers a custom command type as safe for execution.
        /// </summary>
        public static void RegisterSafeCommandType(Type commandType)
        {
            if (commandType == null)
                throw new ArgumentNullException(nameof(commandType));

            if (!typeof(ICommand).IsAssignableFrom(commandType))
                throw new ArgumentException("Type must implement ICommand", nameof(commandType));

            AllowedCommandTypes.Add(commandType);
        }
    }
}