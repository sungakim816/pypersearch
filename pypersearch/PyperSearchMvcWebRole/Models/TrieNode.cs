using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PyperSearchMvcWebRole.Models
{
    public class TrieNode
    {
        public bool IsWord { get; set; }

        public Dictionary<char, TrieNode> Children { get; set; }
        public TrieNode()
        {
            Children = new Dictionary<char, TrieNode>();
            IsWord = false;
        }


    }
}