// Modified Version of symspell.
// Used in conjunction with modified-Kneser-Ney prediction for auto-completion of text.

// Copyright (C) 2015 Wolf Garbe
// Version: 3.0
// Author: Wolf Garbe <wolf.garbe@faroo.com>
// Maintainer: Wolf Garbe <wolf.garbe@faroo.com>
// URL: http://blog.faroo.com/2012/06/07/improved-edit-distance-based-spelling-correction/
// Description: http://blog.faroo.com/2012/06/07/improved-edit-distance-based-spelling-correction/
//
// License:
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License, 
// version 3.0 (LGPL-3.0) as published by the Free Software Foundation.
// http://www.opensource.org/licenses/LGPL-3.0
//
// Usage: single word + Enter:  Display spelling suggestions
//        Enter without input:  Terminate the program


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

internal class DictionaryItem
{
    public int[] Suggestions { get; set; }
    public Int32 Count { get; set; }

    public DictionaryItem()
    {
        Suggestions = new int[0];
    }

    public void ClearSuggestions()
    {
        Suggestions = new int[0];
    }

    public void AddSuggestion(int value)
    {
        var temp = Suggestions;
        Suggestions = new int[temp.Length + 1];
        for (var x = 0; x < temp.Length; x++)
        {
            Suggestions[x] = temp[x];
        }

        Suggestions[Suggestions.Length - 1] = value;
    }
}

    /// <see cref="http://github.com/jeanbern/symspell">
    /// Maintained in a branch to comply with licensing terms.
    /// </see>
    internal sealed class SymmetricPredictor
    {
        public Dictionary<string, double> PrefixLookup(string input, string language, int maxResults)
        {
            var iLen = input.Length;
            var maxDeletes = (input.Length - 1) / 3;

            var deletes = maxDeletes < 1 ? new HashSet<string> { input } : Edits(input, maxDeletes, new HashSet<string>());
            var resultPositions = new HashSet<string>();
            foreach (var delete in deletes)
            {
                DictionaryItem di;
                if (!Dictionary.TryGetValue(language + delete, out di)) {continue;}
                foreach (var word in di.Suggestions.Select(index => Wordlist[index]).Where(word => word.Length >= iLen))
                {
                    resultPositions.Add(word);
                }
            }

            if (Wordlist.Contains(input)) { resultPositions.Add(input); }
            maxResults = Math.Min(maxResults, resultPositions.Count);
            //TODO: may want to use x.Count somewhere in the value section. Smoothing it would help. As of right now it's way too influential, so I'll let the Auger deal with frequency.
            var res = resultPositions.ToDictionary(x => x, x => JaroWinkler.Distance(x, input)).OrderByDescending(x => x.Value/*Dictionary[x.Key].Count*/).Take(maxResults).ToDictionary(x => x.Key, x => x.Value);
            return res;
        }

        internal SymmetricPredictor(Dictionary<string, DictionaryItem> dictionary, List<string> wordlist)
        {
            Dictionary = dictionary;
            Wordlist = wordlist;
        }

        internal SymmetricPredictor(IEnumerable<KeyValuePair<string, int>> strings, string language)
        {
            foreach (var key in strings)
            {
                CreateDictionaryEntry(key.Key, language, key.Value);
            }
            
            Wordlist.TrimExcess();
        }
        
        //Dictionary that contains both the original words and the deletes derived from them. A term might be both word and delete from another word at the same time.
        //For space reduction a item might be either of type dictionaryItem or Int. 
        //A dictionaryItem is used for word, word/delete, and delete with multiple suggestions. Int is used for deletes with a single suggestion (the majority of entries).
        internal readonly Dictionary<string, DictionaryItem> Dictionary = new Dictionary<string, DictionaryItem>(); //initialisierung

        //List of unique words. By using the suggestions (Int) as index for this list they are translated into the original string.
        internal readonly List<string> Wordlist = new List<string>();

        //Only include prefixes for words that show up at least x times.
        private const int PrefixThreshold = 0;

        //Only include edits for words that show up at least x times.
        private const int IncludeThreshold = 0;

        //for every word there all deletes with an edit distance of 1..editDistanceMax created and added to the dictionary
        //every delete entry has a suggestions list, which points to the original term(s) it was created from
        //The dictionary may be dynamically updated (word frequency and new words) at any time by calling createDictionaryEntry
        private bool CreateDictionaryEntry(string key, string language, int count = 1)
        {
            var newFind = true;
            DictionaryItem value;
            if (Dictionary.TryGetValue(language + key, out value))
            {
                if (value.Count == 0)
                {
                    //This would mean the word was added as a prefix or deletion for another.
                    //By assigning it a count we mark it a real word.
                    value.Count = count;
                }
                else
                {
                    //This shouldn't really ever happen. Our only constructor takes KeyValuePairs representing a word and it's count.
                    newFind = false;
                    //prevent overflow
                    if (value.Count < Int32.MaxValue - count)
                    {
                        value.Count += count;
                    }
                    else
                    {
                        value.Count = Int32.MaxValue;
                    }
                }
            }
            else if (Wordlist.Count < Int32.MaxValue)
            {
                value = new DictionaryItem { Count = count };
                Dictionary.Add(language + key, value);
            }
            else
            {
                throw new OverflowException("The wordlist count would be greater than Int32.MaxValue, we can't add any more words.");
            }

            if (!newFind) return false;

            //edits/suggestions are created only once, no matter how often word occurs
            //edits/suggestions are created only as soon as the word occurs in the corpus, 
            //even if the same term existed before in the dictionary as an edit from another word
            Wordlist.Add(key);
            var keyint = Wordlist.Count - 1;

            //use a threshold so that very rare words are not corrected but will still count as correct when typed out
            if (count < IncludeThreshold) {return true;}

            //another threshold can be used to determine if a word shows up frequently enough to have it's prefixes included.
            //It will still have a small amount of prefix guessing just due to possibility of having the deletes at the end.
            //TODO: this could be a sliding scale so that longer words do not have super short prefixes included unless they are very common.
            if (count < PrefixThreshold) {return true;}
            
            //create deletes
            var edits = PrefixesAndDeletes(key, new HashSet<string>(), 1);
            foreach (var delete in edits)
            {
                DictionaryItem di;
                if (Dictionary.TryGetValue(language + delete, out di))
                {
                    //already exists:
                    //1. word1==deletes(word2) 
                    //2. deletes(word1)==deletes(word2) 
                    if (!di.Suggestions.Contains(keyint))
                    {
                        di.AddSuggestion(keyint);
                    }
                }
                else
                {
                    di = new DictionaryItem { Count = 0 };
                    di.AddSuggestion(keyint);
                    Dictionary.Add(language + delete, di);
                }
            }

            return true;
        }

        private static IEnumerable<string> PrefixesAndDeletes(string word, HashSet<string> deletes, int minPrefixLength = 1)
        {
            for (var x = word.Length; x >= minPrefixLength; x--)
            {
                var prefix = word.Substring(0, x);
                //maybe 5 instead of 4
                var maxDeletes = prefix.Length > 4 ? 2 : prefix.Length > 1 ? 1 : 0;
                Edits(prefix, maxDeletes, deletes);
            }

            return deletes;
        }

        //inexpensive and language independent: only deletes, no transposes + replaces + inserts
        //replaces and inserts are expensive and language dependent (Chinese has 70,000 Unicode Han characters)
        private static HashSet<string> Edits(string word, int editDistanceRemaining, HashSet<string> deletes)
        {
            editDistanceRemaining--;
            if (word.Length <= 1 || editDistanceRemaining < 0) { return deletes; }

            for (var i = 0; i < word.Length; i++)
            {
                var delete = word.Remove(i, 1);
                if (deletes.Add(delete) && editDistanceRemaining > 0)
                {
                    //recursion, if maximum edit distance not yet reached
                    Edits(delete, editDistanceRemaining, deletes);
                }
            }

            return deletes;
        }
    }
}
