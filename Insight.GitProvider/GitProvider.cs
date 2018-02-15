﻿using Insight.Shared;
using Insight.Shared.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Insight.GitProvider
{
    public class GitProvider : ISourceControlProvider
    {
        private string _startDirectory;
        private string _cachePath;
        private string _workItemRegex;
        private string _gitHistoryExportFile;
        private string _historyBinCacheFile;
        private GitCommandLine _gitCli;

        public void Initialize(string projectBase, string cachePath, string workItemRegex)
        {
            _startDirectory = projectBase;
            _cachePath = cachePath;
            _workItemRegex = workItemRegex;

            // TODO really needd?
            _gitHistoryExportFile = Path.Combine(cachePath, @"git_history.log");
            _historyBinCacheFile = Path.Combine(cachePath, @"cs_history.bin");
            _gitCli = new GitCommandLine(_startDirectory);

        }
        public static string GetClass()
        {
            var type = typeof(GitProvider);
            return type.FullName + "," + type.Assembly.GetName().Name;
        }

        public Dictionary<string, int> CalculateDeveloperWork(Artifact artifact)
        {
            throw new NotImplementedException();
        }

        public List<FileRevision> ExportFileHistory(string localFile)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// You need to call UpdateCache before.
        /// </summary>
        public ChangeSetHistory QueryChangeSetHistory()
        {
            if (!File.Exists(_historyBinCacheFile))
            {
                var msg = $"History cache file '{_historyBinCacheFile}' not found. You have to 'Sync' first.";
                throw new FileNotFoundException(msg);
            }

            var binFile = new BinaryFile<ChangeSetHistory>();
            return binFile.Read(_historyBinCacheFile);
        }

        bool GoToNextRecord(StreamReader reader)
        {
            
            if (_lastLine == recordMarker)
            {
                // We are already positioned on the next changeset.
                return true;
            }
            string line;
            while ((line = ReadLine(reader)) != null)
            {
                if (line.Equals(recordMarker))
                {
                    return true;
                }
            }

            return false;
        }

        private string _lastLine;
        string ReadLine(StreamReader reader)
        {
            // The only place where we read
            _lastLine = reader.ReadLine()?.Trim();
            return _lastLine;
        }

     
        /// <summary>
        /// Log file has format specified in GitCommandLine class
        /// </summary>
        private ChangeSetHistory ParseLog(string logFile)
        {
          
            var changeSets = new List<ChangeSet>();

            using (var fs = new FileStream(logFile, FileMode.Open))
            {
                using (var reader = new StreamReader(fs))
                {
                    var proceed = GoToNextRecord(reader);
                    if (!proceed)
                    {
                        throw new FormatException("The file does not contain any change sets.");
                    }

                    while (proceed)
                    {
                        var changeSet = ParseRecord(reader);
                        changeSets.Add(changeSet);
                        proceed = GoToNextRecord(reader);
                    }
                }
            }

            var history = new ChangeSetHistory(changeSets);
            return history;
        }

        string recordMarker = "START_HEADER";
        string endHeaderMarker = "END_HEADER";
        private ChangeSet ParseRecord(StreamReader reader)
        {
            

            // We are located on the first data item of the record
            var shortHash = ReadLine(reader);
            var committer = ReadLine(reader);
            var date = ReadLine(reader);

            var commentBuilder = new StringBuilder();
            string commentLine;
          
            while ((commentLine = ReadLine(reader)) != endHeaderMarker)
            {
    
                if (!string.IsNullOrEmpty(commentLine))
                {
                    commentBuilder.AppendLine(commentLine);
                }
            }

            var cs = new ChangeSet();
            cs.Id = int.Parse(shortHash, NumberStyles.HexNumber);
            cs.Committer = committer;
            cs.Comment = commentBuilder.ToString().Trim(new[] { '\r', '\n' }); 
            cs.Date = DateTime.Parse(date);

            Debug.Assert(commentLine == endHeaderMarker);

          
            // Now parse the files!
            string changeItem = ReadLine(reader);
            while (changeItem != null && changeItem != recordMarker)
            {
                if (!string.IsNullOrEmpty(changeItem))
                {
                    var ci = new ChangeItem();

                    // Example
                    // M Visualization.Controls/Strings.resx
                    // A Visualization.Controls/Tools/IHighlighting.cs
                    // R083 Visualization.Controls/Filter/FilterView.xaml   Visualization.Controls/Tools/ToolView.xaml

                    var parts = changeItem.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    KindOfChange changeKind = ToKindOfChange(parts[0]);
                    ci.Kind = changeKind;
                    if (changeKind == KindOfChange.Rename)
                    {
                        Debug.Assert(parts.Length == 3);
                        var oldName = parts[1];
                        var newName = parts[2];
                        ci.ServerPath = newName;
                    }
                    else
                    {
                        Debug.Assert(parts.Length == 2 || parts.Length == 3);
                        ci.ServerPath = parts[1];
                    }

                    ci.LocalPath = MapToLocalFile(ci.ServerPath);

                    // TODO
                    ci.Id = new StringId(ci.ServerPath);
                    cs.Items.Add(ci);
                }
                changeItem = ReadLine(reader);

            }

           
            return cs;
        }

        private string MapToLocalFile(string serverPath)
        {
            // In git we have the restriction 
            // that we cannot choose any sub directory.
            // (Current knowledge). Select the one with .git for the moment.

            // Example
            // _startDirectory = d:\\....\Insight
            // serverPath = Insight/Board.txt
            // localPath = d:\\....\Insight\Insight/Board.txt
            var serverNormalized = serverPath.Replace("/", "\\");
            var localPath = Path.Combine(_startDirectory, serverNormalized);
            return localPath;
        }

        private KindOfChange ToKindOfChange(string kind)
        {
            if (kind.StartsWith("R"))
            {
                // The next number is the similarity with the original file
                var similarityWithOriginal = int.Parse(kind.Substring(1));
                if (similarityWithOriginal < 90)
                {
                    return KindOfChange.Add;
                }

                return KindOfChange.Rename;
            }
            else if (kind == "A")
            {
                return KindOfChange.Add;
            }
            else if (kind == "M")
            {
                return KindOfChange.Edit;
            }
            else
            {
                return KindOfChange.None;
            }
        }

        public void UpdateCache()
        {
            // Git has the complete history locally anyway.
            // So we just can fetch and pull any changes.

            // TODO
            //AbortOnPotentialMergeConflicts();
            //_gitCli.PullMasterFromOrigin();
            var log = _gitCli.Log();
            File.WriteAllText(_gitHistoryExportFile, log);
           
            var history = ParseLog(_gitHistoryExportFile);

            var binFile = new BinaryFile<ChangeSetHistory>();
            binFile.Write(_historyBinCacheFile, history);
        }

        /// <summary>
        /// I don't want to run into merge conflicts.
        /// Abort if there are local changes to the working or staging area.
        /// Abort if there are local commits not pushed to the remote.
        /// </summary>
        private void AbortOnPotentialMergeConflicts()
        {
            if (_gitCli.HasLocalChanges())
            {
                throw new Exception("Abort. There are local changes.");
            }

            if (_gitCli.HasLocalCommits())
            {
                throw new Exception("Abort. There are local commits.");
            }
        }
    }
}
