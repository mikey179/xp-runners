﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Net.XpFramework.Runner
{
    delegate string Argument(string arg);

    class Executor
    {
        private static string PATH_SEPARATOR = new string(new char[] { Path.PathSeparator});
        private static List<string> EMPTY_LIST = new List<string>();

        /// <summary>
        /// Encodes a given string for use in a command line argument. The returned
        /// string is enclosed in double quotes. Double quotes and backslashes have
        /// been escaped.
        /// </summary>
        private static string Pass(string arg)
        {
            var ret = new StringBuilder();

            ret.Append('"');
            for (var i = 0; i < arg.Length; i++)
            {
                if ('"' == arg[i])
                {
                    ret.Append("\"\"");     // Double-quote -> double double-quote
                }
                else if ('\\' == arg[i])
                {
                    ret.Append("\\\\");     // Backslash -> double backslash
                }
                else
                {
                    ret.Append(arg[i]);
                }
            }
            ret.Append('"');

            return ret.ToString();
        }

        /// <summary>
        /// Encodes a given string for use in a command line argument, using UTF-8.
        /// bytes. The returned string is enclosed in double quotes. Double quotes 
        /// and backslashes have been escaped.
        /// </summary>
        private static string Encode(string arg)
        {
            var bytes = Encoding.UTF8.GetBytes(arg);
            var ret = new StringBuilder();

            ret.Append('"');
            for (var i = 0; i < bytes.Length; i++)
            {
                if (34 == bytes[i])
                {
                    ret.Append("\"\"");     // Double-quote -> double double-quote
                }
                else if (92 == bytes[i])
                {
                    ret.Append("\\\\");     // Backslash -> double backslash
                }
                else
                {
                    ret.Append(Convert.ToString((char)bytes[i]));
                }
            }
            ret.Append('"');

            return ret.ToString();
        }

        /// <summary>
        /// Creates the executor process instance
        /// </summary>
        public static Process Instance(string base_dir, string runner, string tool, string[] includes, string[] args)
        {
            string home = Environment.GetEnvironmentVariable("HOME");

            // Read configuration
            XpConfigSource configs = new CompositeConfigSource(
                new EnvironmentConfigSource(),
                new IniConfigSource(new Ini(Paths.Compose(".", "xp.ini"))),
                null != home ? new IniConfigSource(new Ini(Paths.Compose(home, ".xp", "xp.ini"))) : null,
                new IniConfigSource(new Ini(Paths.Compose(Environment.SpecialFolder.LocalApplicationData, "Xp", "xp.ini"))),
                new IniConfigSource(new Ini(Paths.Compose(base_dir, "xp.ini")))
            );

            IEnumerable<string> use_xp = configs.GetUse();
            string runtime = configs.GetRuntime();
            string executor = configs.GetExecutable(runtime) ?? "php";
            bool wmain = configs.GetWMain(runtime) ?? false;
            
            if (null == use_xp)
            {
                throw new EntryPointNotFoundException("Cannot determine use_xp setting from " + configs);
            }
        
            // Pass "USE_XP" and includes inside include_path separated by two path 
            // separators. Prepend "." for the oddity that if the first element does
            // not exist, PHP scraps all the others(!)
            //
            // E.g.: -dinclude_path=".;xp\5.7.0;..\dialog;;..\impl.xar;..\log.xar"
            //                       ^ ^^^^^^^^^^^^^^^^^^  ^^^^^^^^^^^^^^^^^^^^^^
            //                       | |                   include_path
            //                       | USE_XP
            //                       Dot
            string argv = String.Format(
                "-C -q -dinclude_path=\".{1}{0}{1}{1}{2}\" -dmagic_quotes_gpc=0",
                String.Join(PATH_SEPARATOR, new List<string>(use_xp).ToArray()),
                PATH_SEPARATOR,
                String.Join(PATH_SEPARATOR, includes)
            );
            
            // Look for PHP configuration
            foreach (KeyValuePair<string, IEnumerable<string>> kv in configs.GetArgs(runtime))
            {
                foreach (string value in kv.Value)
                {
                    argv += " -d" + kv.Key + "=\"" + value + "\"";
                }
            }

            // Add extensions
            IEnumerable<string> extensions= configs.GetExtensions(runtime);
            if (null != extensions)
            {
                foreach (var ext in extensions)
                {
                    argv += " -dextension=" + ext;
                }
            }

            // Find entry point, which is either a file called [runner]-main.php, which
            // will receive the arguments in UTF-8, or a file called [runner].php, which
            // assumes the arguments come in in platform encoding.
            string entry = null;
            Argument argument = Pass;
            if (null != (entry = Paths.Find(use_xp, "tools\\" + runner + "-main.php")))
            {

                // Windows encodes the command line arguments to platform encoding for PHP,
                // which doesn't define a "wmain()", so we'll need to double-encode our $argv
                // here. In case this changes, and this is a very long shot at PHP's future,
                // the "wmain" configuration option must be set in the [runtime] section,
                // and we'll leave the argument as-is; assuming PHP will internally convert 
                // it to something useful (e.g. "utf-8") and indicate this to userland. 
                if (!wmain)
                {
                    argument = Encode;
                    argv += " -dencoding=utf-8";
                }
                else
                {
                    argv += " -dencoding=" + Encoding.Default.HeaderName.Replace("Windows-", "cp");
                }

                // Pass Console input and output encoding; but only inside real console windows.
                //
                // See http://msdn.microsoft.com/en-us/library/system.text.encoding.headername.aspx
                // and http://msdn.microsoft.com/en-us/library/system.text.encoding.aspx
                if (null == Environment.GetEnvironmentVariable("TERM")) 
                {
                    // argv += "," + Console.InputEncoding.HeaderName.Replace("Windows-", "cp") + "," + Console.OutputEncoding.HeaderName.Replace("Windows-", "cp");
                    // Console.OutputEncoding = Encoding.UTF8;
                    // argv += ",utf-16,utf-16";
                }
            }
            else if (null != (entry = Paths.Find(use_xp, "tools\\" + runner + ".php")))
            {
                // Pass
            }
            else
            {
                throw new EntryPointNotFoundException("Cannot find tool in " + use_xp);
            }

            // Spawn runtime
            var proc = new Process();
            proc.StartInfo.FileName = executor;
            proc.StartInfo.Arguments = argv + " \"" + entry + "\" " + tool;
            if (args.Length > 0)
            {
                foreach (string arg in args) 
                {
                    // Console.Error.Write("ARG '{0}' = `", arg);
                    // var bytes = Encoding.UTF8.GetBytes(arg);
                    // for (var i = 0; i < bytes.Length; i++)
                    // {
                    //    Console.Error.Write("\\{0}", Convert.ToString(bytes[i], 8));
                    // }
                    // Console.Error.WriteLine('`');
                    proc.StartInfo.Arguments +=  " " + argument(arg);
                }
            }

            // Catch Ctrl+C (only works in "real" consoles, not in a cygwin 
            // shell, for example) and kill the spawned process, see also:
            // http://www.cygwin.com/ml/cygwin/2006-12/msg00151.html
            // http://www.mail-archive.com/cygwin@cygwin.com/msg74638.html
            Console.CancelKeyPress += delegate {
                proc.Kill();
                proc.WaitForExit();
            };
            
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;

            // Route output through this command. This way, we prevent 
            // PHP garbling the output on a Windows console window.
            proc.OutputDataReceived += new DataReceivedEventHandler(delegate(object sendingProcess, DataReceivedEventArgs outLine)
            {
                if (null != outLine.Data) Console.Out.WriteLine(outLine.Data);
            });
            proc.ErrorDataReceived += new DataReceivedEventHandler(delegate(object sendingProcess, DataReceivedEventArgs outLine)
            {
                if (null != outLine.Data) Console.Error.WriteLine(outLine.Data);
            });

            return proc;
        }

        /// <summary>
        /// Creates and runs the executor instance. Returns the process' exitcode.
        /// </summary>
        public static int Execute(string base_dir, string runner, string tool, string[] includes, string[] args)
        {
            var encoding = Console.OutputEncoding;
            Console.OutputEncoding = Encoding.UTF8;

            var proc = Instance(base_dir, runner, tool, includes, args);
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                return proc.ExitCode;
            }
            catch (SystemException e) 
            {
                throw new ExecutionEngineException(proc.StartInfo.FileName + ": " + e.Message, e);
            } 
            finally
            {
                Console.OutputEncoding = encoding;
                proc.Close();
            }
        }
    }
}
