﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Insight.Shared;
using Insight.Shared.Extensions;
using Insight.Shared.Model;
using Insight.Shared.VersionControl;

namespace Insight.GitProvider
{
    /// <summary>
    /// Provides higher level funtions and queries on a git repository.
    /// </summary>
    public sealed class GitProvider : ISourceControlProvider
    {
        private static readonly Regex _regex = new Regex(@"\\(?<Value>[a-zA-Z0-9]{3})", RegexOptions.Compiled);
        private readonly string endHeaderMarker = "END_HEADER";

        private readonly string recordMarker = "START_HEADER";
        private string _cachePath;
        private GitCommandLine _gitCli;
        private string _gitHistoryExportFile;

        private string _lastLine;
        private string _startDirectory;
        private string _workItemRegex;

        public static string GetClass()
        {
            var type = typeof(GitProvider);
            return type.FullName + "," + type.Assembly.GetName().Name;
        }

        public Dictionary<string, int> CalculateDeveloperWork(Artifact artifact)
        {
            var annotate = _gitCli.Annotate(artifact.LocalPath);

            //S = not a whitespace
            //s = whitespace

            // Parse annotated file
            var workByDevelopers = new Dictionary<string, int>();
            var changeSetRegex = new Regex(@"^\S*\t\(\s+(?<developerName>\S+).*", RegexOptions.Compiled | RegexOptions.Multiline);

            // Work by changesets (line by line)
            var matches = changeSetRegex.Matches(annotate);
            foreach (Match match in matches)
            {
                var developer = match.Groups["developerName"].Value;
                workByDevelopers.AddToValue(developer, 1);
            }

            return workByDevelopers;
        }

        // TODO that seems unreliable
        public string Decoder(string value)
        {
            var replace = _regex.Replace(
                                         value,
                                         m => ((char)int.Parse(m.Groups["Value"].Value, NumberStyles.HexNumber)).ToString()
                                        );
            return replace.Trim('"');
        }

        public List<FileRevision> ExportFileHistory(string localFile)
        {
            var result = new List<FileRevision>();

            var xml = _gitCli.Log(localFile);

            /*
             * TODO
             
          

            var dom = new XmlDocument();
            dom.LoadXml(xml);
            var entries = dom.SelectNodes("//logentry");

            if (entries == null)
            {
                return result;
            }

            foreach (XmlNode entry in entries)
            {
                if (entry?.Attributes == null)
                {
                    continue;
                }

                var revision = int.Parse(entry.Attributes["revision"].Value);
                var date = entry.SelectSingleNode("./date")?.InnerText;
                var dateTime = DateTime.Parse(date);

                // Get historical version from file cache

                var fi = new FileInfo(localFile);
                var exportFile = GetPathToExportedFile(fi, revision);

                // Download if not already in cache
                if (!File.Exists(exportFile))
                {
                    _svnCli.ExportFileRevision(localFile, revision, exportFile);
                }

                result.Add(new FileRevision(localFile, revision, dateTime, exportFile));
            }

            return result;
            */
            throw new NotImplementedException();
        }

        public void Initialize(string projectBase, string cachePath, string workItemRegex)
        {
            _startDirectory = projectBase;
            _cachePath = cachePath;
            _workItemRegex = workItemRegex;

            _gitHistoryExportFile = Path.Combine(cachePath, @"git_history.log");
            _gitCli = new GitCommandLine(_startDirectory);
        }

        /// <summary>
        /// You need to call UpdateCache before.
        /// </summary>
        public ChangeSetHistory QueryChangeSetHistory()
        {
            if (!File.Exists(_gitHistoryExportFile))
            {
                var msg = $"Log export file '{_gitHistoryExportFile}' not found. You have to 'Sync' first.";
                throw new FileNotFoundException(msg);
            }

            return ParseLog(_gitHistoryExportFile);
        }

        public void UpdateCache()
        {
            if (!Directory.Exists(Path.Combine(_startDirectory, ".git")))
            {
                // We need the root (containing .git) because of the function MapToLocalFile.
                // We could go upwards and find the git root ourself and use this root for the path mapping.
                // But for the moment take everything. The user can set filters in the project settings.
                throw new ArgumentException("The given start directory is not the root of a git repository.");
            }

            // Git has the complete history locally anyway.

            var log = _gitCli.Log();
            File.WriteAllText(_gitHistoryExportFile, log);
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

        private ChangeItem CreateChangeItem(string changeItem, MovementTracker tracker)
        {
            var ci = new ChangeItem();

            // Example
            // M Visualization.Controls/Strings.resx
            // A Visualization.Controls/Tools/IHighlighting.cs
            // R083 Visualization.Controls/Filter/FilterView.xaml   Visualization.Controls/Tools/ToolView.xaml

            var parts = changeItem.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var changeKind = ToKindOfChange(parts[0]);
            ci.Kind = changeKind;
            if (changeKind == KindOfChange.Rename)
            {
                Debug.Assert(parts.Length == 3);
                var oldName = parts[1];
                var newName = parts[2];
                ci.ServerPath = Decoder(newName);
                tracker.TrackId(ci, oldName);
            }
            else
            {
                Debug.Assert(parts.Length == 2 || parts.Length == 3);
                ci.ServerPath = Decoder(parts[1]);
                tracker.TrackId(ci, null);
            }

            ci.LocalPath = MapToLocalFile(ci.ServerPath);
            return ci;
        }

        private bool GoToNextRecord(StreamReader reader)
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


        /// <summary>
        /// Log file has format specified in GitCommandLine class
        /// </summary>
        private ChangeSetHistory ParseLog(string logFile)
        {
            var changeSets = new List<ChangeSet>();
            var tracker = new MovementTracker();

            FileStream stream = null;
            try
            {
                stream = new FileStream(logFile, FileMode.Open);

                using (var reader = new StreamReader(stream))
                {
                    stream = null;
                    var proceed = GoToNextRecord(reader);
                    if (!proceed)
                    {
                        throw new FormatException("The file does not contain any change sets.");
                    }

                    while (proceed)
                    {
                        var changeSet = ParseRecord(reader, tracker);
                        changeSets.Add(changeSet);
                        proceed = GoToNextRecord(reader);
                    }
                }
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            var history = new ChangeSetHistory(changeSets);
            return history;
        }

        private ChangeSet ParseRecord(StreamReader reader, MovementTracker tracker)
        {
            // We are located on the first data item of the record
            var hash = ReadLine(reader);
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
            cs.Id = new StringId(hash);
            cs.Committer = committer;
            cs.Comment = commentBuilder.ToString().Trim('\r', '\n');

            ParseWorkItemsFromComment(cs.WorkItems, cs.Comment);

            cs.Date = DateTime.Parse(date);

            Debug.Assert(commentLine == endHeaderMarker);

            tracker.BeginChangeSet();
            ReadChangeItems(reader, cs, tracker);
            tracker.ApplyChangeSet();
            return cs;
        }

        private void ParseWorkItemsFromComment(List<WorkItem> workItems, string comment)
        {
            if (!string.IsNullOrEmpty(_workItemRegex))
            {
                var extractor = new WorkItemExtractor(_workItemRegex);
                workItems.AddRange(extractor.Extract(comment));
            }
        }
     

        private void ReadChangeItems(StreamReader reader, ChangeSet cs, MovementTracker tracker)
        {
            // Now parse the files!
            var changeItem = ReadLine(reader);
            while (changeItem != null && changeItem != recordMarker)
            {
                if (!string.IsNullOrEmpty(changeItem))
                {
                    var ci = CreateChangeItem(changeItem, tracker);
                    cs.Items.Add(ci);
                }

                changeItem = ReadLine(reader);
            }
        }

        private string ReadLine(StreamReader reader)
        {
            // The only place where we read
            _lastLine = reader.ReadLine()?.Trim();
            return _lastLine;
        }

        private KindOfChange ToKindOfChange(string kind)
        {
            if (kind.StartsWith("R"))
            {
                return KindOfChange.Rename;
            }
            else if (kind == "A")
            {
                return KindOfChange.Add;
            }
            else if (kind == "D")
            {
                return KindOfChange.Delete;
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
    }
}