// SymSpell: 1 million times faster through Symmetric Delete spelling correction algorithm
//
// The Symmetric Delete spelling correction algorithm reduces the complexity of edit candidate generation and dictionary lookup 
// for a given Damerau-Levenshtein distance. It is six orders of magnitude faster and language independent.
// Opposite to other algorithms only deletes are required, no transposes + replaces + inserts.
// Transposes + replaces + inserts of the input term are transformed into deletes of the dictionary term.
// Replaces and inserts are expensive and language dependent: e.g. Chinese has 70,000 Unicode Han characters!
//
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

public class SuggestItem
{
    public readonly string Term = "";
    public int Distance;
    public Int32 Count;

    public SuggestItem(string term)
    {
        Term = term;
    }

    public override bool Equals(object obj)
    {
        return Equals(Term, ((SuggestItem)obj).Term);
    }

    public override int GetHashCode()
    {
        return Term.GetHashCode();
    }
}

public class DictionaryItem
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

internal sealed class SymmetricPredictor
{
// ReSharper disable ConvertToConstant.Local
// These are tweaked manually by developers.
    private readonly int _editDistanceMax = 1;
    private readonly int _verbose = 2;
    //0: top suggestion
    //1: all suggestions of smallest edit distance 
    //2: all suggestions <= editDistanceMax (slower, no early termination) - added early termination if Suggestions >= SuggestionsMin
    private readonly bool _prefixDictionary = true;
// ReSharper restore ConvertToConstant.Local
        
    internal SymmetricPredictor(IEnumerable<KeyValuePair<string, int>> strings, string language)
    {
        Console.Write("Creating dictionary ...");
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var wordCount = strings.LongCount(key => CreateDictionaryEntry(key.Key, language, key.Value));

        Wordlist.TrimExcess();
        stopWatch.Stop();
        Console.WriteLine("\rDictionary: " + wordCount.ToString("N0") + " words, " + Dictionary.Count.ToString("N0") +
                            " entries, edit distance=" + _editDistanceMax + " in " + stopWatch.ElapsedMilliseconds + "ms " +
                            (Process.GetCurrentProcess().PrivateMemorySize64 / 1000000).ToString("N0") + " MB");
    }

    //Dictionary that contains both the original words and the deletes derived from them. A term might be both word and delete from another word at the same time.
    //For space reduction a item might be either of type dictionaryItem or Int. 
    //A dictionaryItem is used for word, word/delete, and delete with multiple suggestions. Int is used for deletes with a single suggestion (the majority of entries).
    internal readonly Dictionary<string, DictionaryItem> Dictionary = new Dictionary<string, DictionaryItem>(); //initialisierung

    //List of unique words. By using the suggestions (Int) as index for this list they are translated into the original string.
    internal readonly List<string> Wordlist = new List<string>();
        
    internal int Maxlength = 0;//maximum dictionary term length

    //for every word there all deletes with an edit distance of 1..editDistanceMax created and added to the dictionary
    //every delete entry has a suggestions list, which points to the original term(s) it was created from
    //The dictionary may be dynamically updated (word frequency and new words) at any time by calling createDictionaryEntry
    private bool CreateDictionaryEntry(string key, string language, int count = 1)
    {
        var newFind = true;
        DictionaryItem value;
        if (Dictionary.TryGetValue(language + key, out value))
        {
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
        else if (Wordlist.Count < Int32.MaxValue)
        {
            value = new DictionaryItem { Count = count };
            Dictionary.Add(language + key, value);

            if (key.Length > Maxlength) {Maxlength = key.Length;}
        }
        else
        {
            throw new OverflowException("The wordlist count would be greater than Int32.MaxValue, we can't add any more words.");
        }

        if (!newFind) return false;

        //edits/suggestions are created only once, no matter how often word occurs
        //edits/suggestions are created only as soon as the word occurs in the corpus, 
        //even if the same term existed before in the dictionary as an edit from another word
        //a treshold might be specifid, when a term occurs so frequently in the corpus that it is considered a valid word for spelling correction
        //word2index
        Wordlist.Add(key);
        var keyint = Wordlist.Count - 1;

        //create deletes
        var edits = _prefixDictionary ? EditsOrPrefixes(key, _editDistanceMax, new HashSet<string>()) : Edits(key, _editDistanceMax, new HashSet<string>());
        foreach (var delete in edits)
        {
            DictionaryItem di;
            if (Dictionary.TryGetValue(language + delete, out di))
            {
                //already exists:
                //1. word1==deletes(word2) 
                //2. deletes(word1)==deletes(word2) 
                //int or dictionaryItem? single delete existed before!
                if (!di.Suggestions.Contains(keyint))
                {
                    AddLowestDistance(di, key, keyint, delete);
                }
            }
            else
            {
                di = new DictionaryItem { Count = 1 };
                di.AddSuggestion(keyint);
                Dictionary.Add(language + delete, di);
            }
        }

        return true;
    }
        
    //save some time and space
    private void AddLowestDistance(DictionaryItem item, string suggestion, Int32 suggestionint, string delete)
    {

        //remove all existing suggestions of higher distance, if verbose<2
        //index2word
        if ((_verbose < 2) && (item.Suggestions.Length > 0) && (Wordlist[item.Suggestions[0]].Length - delete.Length > suggestion.Length - delete.Length))
        {
            item.ClearSuggestions();
        }

        //do not add suggestion of higher distance than existing, if verbose<2
        if ((_verbose == 2) || (item.Suggestions.Length == 0) || (Wordlist[item.Suggestions[0]].Length - delete.Length >= suggestion.Length - delete.Length))
        {
            item.AddSuggestion(suggestionint);
        }
    }

    //inexpensive and language independent: only deletes, no transposes + replaces + inserts
    //replaces and inserts are expensive and language dependent (Chinese has 70,000 Unicode Han characters)
    private static HashSet<string> Edits(string word, int editDistanceRemaining, HashSet<string> deletes)
    {
        editDistanceRemaining--;
        if (word.Length <= 1) { return deletes; }

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

    private static HashSet<string> EditsOrPrefixes(string word, int editDistanceRemaining, HashSet<string> deletes)
    {
        if (word.Length <= 1) { return deletes; }

        //Recursively go through all possible prefixes.
        var prefix = word.Remove(word.Length - 1, 1);
        if (deletes.Add(prefix))
        {
            EditsOrPrefixes(prefix, editDistanceRemaining, deletes);
        }

        //Recursively do the edits of the whole or prefix part.
        //We can introduce some max edit distance according to word size. This will reduce time and memory requirements.
        //TODO: make sure the modified max helps.
        var modifiedEditDistanceRemaining = Math.Min(editDistanceRemaining, word.Length / 2);
        if (modifiedEditDistanceRemaining > 0)
        {
            Edits(word, modifiedEditDistanceRemaining, deletes);
        }

        return deletes;
    }

    public List<string> PrefixLookup(string input, string language, int editDistanceMax, int maxResults)
    {
        DictionaryItem value;
        var found = Dictionary.TryGetValue(language + input, out value);
        if (!found)
        {
            //throw new NotImplementedException("I don't know what to do in this case. It hasn't shown up to blow things up yet.");
            return new List<string>();
        }

        var suggestions = value.Suggestions.Select(x => Wordlist[x]);

        //Only sort if we need to restrict amount - if you want them all and sorted, use int.Max
        return (maxResults == -1 ? suggestions : suggestions.OrderByDescending(x => Dictionary[x].Count).Take(maxResults)).ToList();
    }


    public List<SuggestItem> Lookup(string input, string language, int editDistanceMax)
    {
        //save some time
        if (input.Length - editDistanceMax > Maxlength) return new List<SuggestItem>();

        var candidates = new List<string>();
        var hashset1 = new HashSet<string>();

        var suggestions = new List<SuggestItem>();
        var hashset2 = new HashSet<string>();

        //add original term
        candidates.Add(input);

        while (candidates.Count > 0)
        {
            var candidate = candidates[0];
            candidates.RemoveAt(0);

            //save some time
            //early termination
            //suggestion distance=candidate.distance... candidate.distance+editDistanceMax                
            //if candidate distance is already higher than suggestion distance, than there are no better suggestions to be expected
            if ((_verbose < 2) && (suggestions.Count > 0) && (input.Length - candidate.Length > suggestions[0].Distance))
            {
                return SortSuggestions(suggestions);
            }

            //read candidate entry from dictionary
            DictionaryItem value;
            if (Dictionary.TryGetValue(language + candidate, out value))
            {

                //if count>0 then candidate entry is correct dictionary term, not only delete item
                if ((value.Count > 0) && hashset2.Add(candidate))
                {
                    //add correct dictionary term term to suggestion list
                    var si = new SuggestItem(candidate)
                    {
                        Count = value.Count,
                        Distance = input.Length - candidate.Length
                    };

                    suggestions.Add(si);
                    //early termination
                    if ((_verbose < 2) && (input.Length - candidate.Length == 0))
                    {
                        return SortSuggestions(suggestions);
                    }
                }

                //iterate through suggestions (to other correct dictionary items) of delete item and add them to suggestion list
                foreach (var suggestionint in value.Suggestions)
                {
                    //save some time 
                    //skipping double items early: different deletes of the input term can lead to the same suggestion
                    //index2word
                    var suggestion = Wordlist[suggestionint];
                    if (!hashset2.Add(suggestion)) { continue; }

                    //True Damerau-Levenshtein Edit Distance: adjust distance, if both distances>0
                    //We allow simultaneous edits (deletes) of editDistanceMax on on both the dictionary and the input term. 
                    //For replaces and adjacent transposes the resulting edit distance stays <= editDistanceMax.
                    //For inserts and deletes the resulting edit distance might exceed editDistanceMax.
                    //To prevent suggestions of a higher edit distance, we need to calculate the resulting edit distance, if there are simultaneous edits on both sides.
                    //Example: (bank==bnak and bank==bink, but bank!=kanb and bank!=xban and bank!=baxn for editDistanceMaxe=1)
                    //Two deletes on each side of a pair makes them all equal, but the first two pairs have edit distance=1, the others edit distance=2.
                    var distance = 0;
                    if (suggestion != input)
                    {
                        if (suggestion.Length == candidate.Length)
                        {
                            distance = input.Length - candidate.Length;
                        }
                        else if (input.Length == candidate.Length)
                        {
                            distance = suggestion.Length - candidate.Length;
                        }
                        else
                        {
                            //https://github.com/jeanbern/SoftWx.Match/blob/master/SoftWx.Match/EditDistance.cs
                            distance = EditDistance.Distance(suggestion, input, _editDistanceMax);
                        }
                    }

                    //save some time.
                    //remove all existing suggestions of higher distance, if verbose<2
                    if ((_verbose < 2) && (suggestions.Count > 0) && (suggestions[0].Distance > distance))
                    {
                        suggestions.Clear();
                    }

                    //do not process higher distances than those already found, if verbose<2
                    if ((_verbose < 2) && (suggestions.Count > 0) && (distance > suggestions[0].Distance)) { continue; }
                    if (distance > editDistanceMax) { continue; }

                    DictionaryItem value2;
                    if (!Dictionary.TryGetValue(language + suggestion, out value2)) { continue; }

                    var si = new SuggestItem(suggestion)
                    {
                        Count = value2.Count,
                        Distance = distance
                    };

                    suggestions.Add(si);
                }//end foreach
            }//end if         

            //add edits 
            //derive edits (deletes) from candidate (input) and add them to candidates list
            //this is a recursive process until the maximum edit distance has been reached
            if (input.Length - candidate.Length >= editDistanceMax) { continue; }
            //save some time
            //do not create edits with edit distance smaller than suggestions already found
            if ((_verbose < 2) && (suggestions.Count > 0) && (input.Length - candidate.Length >= suggestions[0].Distance)) { continue; }

            candidates.AddRange(candidate.Select((t, i) => candidate.Remove(i, 1)).Where(hashset1.Add));
        }//end while

        //sort by ascending edit distance, then by descending word frequency
        return SortSuggestions(suggestions);
    }

    private List<SuggestItem> SortSuggestions(List<SuggestItem> suggestions)
    {
        if (_verbose < 2)
        {
            suggestions.Sort((x, y) => -x.Count.CompareTo(y.Count));
        }
        else
        {
            suggestions.Sort((x, y) => 2 * x.Distance.CompareTo(y.Distance) - x.Count.CompareTo(y.Count));
        }

        if (_verbose == 0 && suggestions.Count > 1)
        {
            return suggestions.GetRange(0, 1);
        }

        return suggestions;
    }
}
