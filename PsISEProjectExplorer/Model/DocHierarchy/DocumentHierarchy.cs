﻿using PsISEProjectExplorer.Enums;
using PsISEProjectExplorer.FullText;
using PsISEProjectExplorer.Model.DocHierarchy.Nodes;
using System;
using System.Collections.Generic;
using System.IO;

namespace PsISEProjectExplorer.Model.DocHierarchy
{
    public class DocumentHierarchy
    {
        public static object RootLockObject = new object();

        public INode RootNode { get; private set; }

        private FullTextDirectory FullTextDirectory { get; set; }

        private IDictionary<string, INode> NodeMap { get; set; }

        public DocumentHierarchy(INode rootNode)
        {
            this.RootNode = rootNode;
            this.FullTextDirectory = new FullTextDirectory();
            this.NodeMap = new Dictionary<string, INode> {{rootNode.Path, rootNode}};
        }

        public INode GetNode(string path)
        {
            INode value;
            this.NodeMap.TryGetValue(path, out value);
            return value;
        }

        public void RemoveNode(INode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            var lockObject = node.Parent == null ? RootLockObject : node.Parent;
            lock (lockObject)
            {
                if (node.Path != this.RootNode.Path)
                {
                    this.NodeMap.Remove(node.Path);
                    this.FullTextDirectory.DeleteDocument(node.Path);
                    node.Remove();
                }
                var itemsToBeRemoved = new List<INode>(node.Children);
                foreach (INode child in itemsToBeRemoved)
                {
                    this.RemoveNode(child);
                }
            }
        }


        public INode CreateNewDirectoryNode(string absolutePath, INode parent, string errorMessage)
        {
            var lockObject = parent == null ? RootLockObject : parent;
            lock (lockObject)
            {
                string name = absolutePath.Substring(absolutePath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                INode node;
                node = new DirectoryNode(absolutePath, name, parent, errorMessage);
                this.NodeMap.Add(absolutePath, node);
                this.FullTextDirectory.DocumentCreator.AddDirectoryEntry(absolutePath, name);
                return node;
            }
        }

        public INode CreateNewFileNode(string absolutePath, string fileContents, INode parent, string errorMessage)
        {
            var lockObject = parent == null ? RootLockObject : parent;
            lock (lockObject)
            {
                string fileName = Path.GetFileName(absolutePath);
                INode fileNode = new FileNode(absolutePath, fileName, parent, errorMessage);
                this.NodeMap.Add(absolutePath, fileNode);
                this.FullTextDirectory.DocumentCreator.AddFileEntry(absolutePath, fileName, fileContents);
                return fileNode;
            }
        }

        public INode CreateNewFunctionNode(string path, PowershellFunction func, INode parent)
        {
            var lockObject = parent == null ? RootLockObject : parent;
            lock (lockObject)
            {
                INode functionNode = new PowershellFunctionNode(path, func, parent);
                this.NodeMap.Add(functionNode.Path, functionNode);
                this.FullTextDirectory.DocumentCreator.AddFunctionEntry(functionNode.Path, func.Name);
                return functionNode;
            }
        }

        public INode UpdateDirectoryNodePath(INode node, string newPath, string errorMessage)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            var lockObject = node.Parent == null ? RootLockObject : node.Parent;
            lock (lockObject)
            {
                this.RemoveNode(node);
                return this.CreateNewDirectoryNode(newPath, node.Parent, errorMessage);
            }
        }

        public INode UpdateFileNodePath(INode node, string newPath, string errorMessage)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            var lockObject = node.Parent == null ? RootLockObject : node.Parent;
            lock (lockObject)
            {
                this.RemoveNode(node);
                return this.CreateNewFileNode(newPath, string.Empty, node.Parent, errorMessage);
            }
        }

        public IEnumerable<SearchResult> SearchNodesFullText(string filter, FullTextFieldType fieldType)
        {
            IList<SearchResult> searchResults = this.FullTextDirectory.Search(filter, fieldType);
            this.AddNodesToSearchResults(searchResults);
            return searchResults;
        }

        public IEnumerable<SearchResult> SearchNodesByTerm(string filter, FullTextFieldType fieldType)
        {
            IList<SearchResult> searchResults = this.FullTextDirectory.SearchTerm(filter, fieldType);
            this.AddNodesToSearchResults(searchResults);
            return searchResults;
        }

        private void AddNodesToSearchResults(IEnumerable<SearchResult> searchResults)
        {
            foreach (SearchResult searchResult in searchResults)
            {
                searchResult.Node = this.GetNode(searchResult.Path);
            }
        }

    }
}
