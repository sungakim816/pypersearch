using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PyperSearchMvcWebRole.Models
{
    public class Trie
    {
        private TrieNode rootNode;
        private List<string> suggestionList;

        public Trie()
        {
            rootNode = new TrieNode();
            suggestionList = new List<string>();
        }

        public void Add(string word)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrWhiteSpace(word))
            {
                return;
            }
            var node = this.rootNode;
            foreach (char letter in word)
            {
                if (!node.Children.ContainsKey(letter))
                {
                    node.Children.Add(letter, new TrieNode());
                }
                node = node.Children[letter];
            }
            node.IsWord = true;
        }

        public void Populate(List<string> words)
        {
            if (!words.Any())
            {
                return;
            }
            foreach (string word in words)
            {
                this.Add(word);
            }
        }

        public bool Contains(string word)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrWhiteSpace(word))
            {
                return false;
            }
            var node = this.rootNode; // start search at root node
            foreach (char letter in word)
            {
                if (node.Children.ContainsKey(letter))
                {
                    return false;
                }
                node = node.Children[letter];
            }
            return true;
        }

        public List<string> Suggestions(string word)
        {
            this.suggestionList.Clear();
            if (string.IsNullOrEmpty(word) || string.IsNullOrWhiteSpace(word))
            {
                return suggestionList;
            }
            
            var node = this.rootNode;
            foreach (char letter in word) // retrieve the node that contains the last letter of the sequence
            {
                node = node.Children[letter];
            }
            this.SuggestionsRecursion(word, node);
            return this.suggestionList;
        }

        private void SuggestionsRecursion(string word, TrieNode node)
        {
            if (node != null && node.IsWord)
            {
                this.suggestionList.Add(word);
            }
            foreach (char letter in word)
            {
                this.SuggestionsRecursion(word + letter, node.Children[letter]);
            }
        }

    }
}