using System;
using System.Collections.Generic;
using System.Linq;

namespace Augury.Spelling
{
    /// <see cref="http://github.com/jeanbern/symspell">
    /// Maintained in a branch to comply with licensing terms.
    /// </see>
    internal sealed class SymmetricPredictor
    {
        public IEnumerable<WordSimilarityNode> PrefixLookup(string input, int maxResults)
        {
            var iLen = input.Length;
            ICollection<string> possibleResults;

            if (iLen < 4)
            {
                HashSet<int> di;
                if (!Dictionary.TryGetValue(input, out di))
                {
                    return Wordlist.Contains(input) ? new[] {new WordSimilarityNode {Word = input, Similarity = 1.0}} : new WordSimilarityNode[0];
                }

                possibleResults = di.Select(index => Wordlist[index]).Where(word => word.Length >= iLen).ToList();
                if (Wordlist.Contains(input)) { possibleResults.Add(input); }
            }
            else
            {
                possibleResults = new HashSet<string>();
                var deletes = new HashSet<string>();
                Edits(input, (iLen - 1)/3, deletes);
                foreach (var delete in deletes)
                {
                    HashSet<int> di;
                    if (!Dictionary.TryGetValue(delete, out di)) { continue; }
                    foreach (var word in di.Select(index => Wordlist[index]).Where(word => word.Length >= iLen))
                    {
                        possibleResults.Add(word);
                    }
                }
            }

            if (possibleResults.Count <= maxResults)
            {
                return possibleResults.Select(word => new WordSimilarityNode { Word = word, Similarity = JaroWinkler.BoundedSimilarity(input, word) });
            }

            var max = Math.Min(possibleResults.Count, maxResults);

            // SortedList.RemoveAt is O(n)
            // SortedDictionary/SortedSet.ElementAt is O(n)
            // So use the Queue!
            var likelyWordsQueue = new WordSimilarityQueue(max);
            var count = 0;
            foreach (var word in possibleResults)
            {
                var jw = JaroWinkler.BoundedSimilarity(input, word);
                if (count < max)
                {
                    ++count;
                    likelyWordsQueue.Enqueue(word, jw);
                }
                else
                {
                    if (jw < likelyWordsQueue.First.Similarity) { continue; }
                    likelyWordsQueue.Dequeue();
                    likelyWordsQueue.Enqueue(word, jw);
                }
            }

            return likelyWordsQueue;
        }

        internal SymmetricPredictor(Dictionary<string, HashSet<int>> dictionary, List<string> wordlist)
        {
            Dictionary = dictionary;
            Wordlist = wordlist;
        }

        internal SymmetricPredictor(IEnumerable<string> strings)
        {
            foreach (var key in strings)
            {
                CreateDictionaryEntry(key);
            }
            
            Wordlist.TrimExcess();
        }

        internal readonly Dictionary<string, HashSet<int>> Dictionary = new Dictionary<string, HashSet<int>>();
        internal readonly List<string> Wordlist = new List<string>();

        private void CreateDictionaryEntry(string key)
        {
            Wordlist.Add(key);
            var keyint = Wordlist.Count - 1;

            HashSet<int> value;
            if (!Dictionary.TryGetValue(key, out value))
            {
                value = new HashSet<int>();
                Dictionary.Add(key, value);
            }

            
            var edits = PrefixesAndDeletes(key);
            foreach (var delete in edits)
            {
                HashSet<int> di;
                if (Dictionary.TryGetValue(delete, out di))
                {
                    di.Add(keyint);
                }
                else
                {
                    di = new HashSet<int> {keyint};
                    Dictionary.Add(delete, di);
                }
            }
        }

        private static IEnumerable<string> PrefixesAndDeletes(string word)
        {
            var wLen = word.Length;
            switch (wLen)
            {
                case 0:
                    return new List<string>();
                case 1:
                    return new List<string>();
                case 2:
                    return new List<string> {word[0].ToString()};
                case 3:
                    var deletes3 = new HashSet<string> {word[0].ToString()};
                    Edits(word, 1, deletes3);
                    return deletes3;
                case 4:
                    var deletes4 = new HashSet<string> {word[0].ToString()};
                    Edits(word.Substring(0, 3), 1, deletes4);
                    Edits(word, 1, deletes4);
                    return deletes4;
                default:
                    var deletes = new HashSet<string> {word[0].ToString()};
                    Edits(word.Substring(0, 3), 1, deletes);
                    Edits(word.Substring(0, 4), 1, deletes);

                    for (var x = wLen; x > 4; x--)
                    {
                        Edits(word.Substring(0, x), 2, deletes);
                    }

                    return deletes;
            }
        }

        private static void Edits(string word, int editDistanceRemaining, ISet<string> deletes)
        {
            if (editDistanceRemaining == 0) { return; }

            var wLen = word.Length;
            if (wLen < 2) { return; }

            if (editDistanceRemaining == 1)
            {
                for (var i = 0; i < wLen; i++)
                {
                    var delete = word.Remove(i, 1);
                    deletes.Add(delete);
                }

                return;
            }

            var newDistance = editDistanceRemaining - 1;
            for (var i = 0; i < wLen; i++)
            {
                var delete = word.Remove(i, 1);
                if (deletes.Add(delete))
                {
                    Edits(delete, newDistance, deletes);
                }
            }
        }
    }
}
