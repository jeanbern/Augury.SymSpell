/*
Licensed to the Apache Software Foundation (ASF) under one or more
contributor license agreements.  See the NOTICE file distributed with
this work for additional information regarding copyright ownership.
The ASF licenses this file to You under the Apache License, Version 2.0
(the "License"); you may not use this file except in compliance with
the License.  You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Linq;

internal static class JaroWinkler
{
    public static double Distance(String s1, String s2)
    {
        var mtp = Matches(s1, s2);

        if (mtp[0] == 0) { return 0f; }

        var m = (double)mtp[0];
        var j = ((m / s1.Length + m / s2.Length + (m - mtp[1]) / m)) / 3;
        var jw = j < Threshold ? j : j + Math.Min(0.1, 1 / (double)mtp[3]) * mtp[2] * (1 - j);
        return jw;
    }

    private const double Threshold = 0.7;
 
    private static int[] Matches(String s1, String s2)
    {
        string max, min;
             
        if (s1.Length > s2.Length)
        {
            max = s1;
            min = s2;
        }
        else
        {
            max = s2;
            min = s1;
        }
 
        var range = Math.Max(max.Length / 2 - 1, 0);
        var matchIndexes = new int[min.Length];
 
        for (var i = 0; i < matchIndexes.Length; i++)
            matchIndexes[i] = -1;
 
        var matchFlags = new bool[max.Length];
        var matches = 0;
             
        for (var mi = 0; mi < min.Length; mi++)
        {
            var c1 = min[mi];
            for (int xi = Math.Max(mi - range, 0), 
                xn = Math.Min(mi + range + 1, max.Length); xi < xn; xi++)
            {
                if (matchFlags[xi] || c1 != max[xi]) {continue;}
 
                matchIndexes[mi] = xi;
                matchFlags[xi] = true;
                matches++;
                break;
            }
        }
             
        var ms1 = new char[matches];
        var ms2 = new char[matches];
 
        for (int i = 0, si = 0; i < min.Length; i++)
        {
            if (matchIndexes[i] == -1) {continue;}
            ms1[si] = min[i];
            si++;
        }
 
        for (int i = 0, si = 0; i < max.Length; i++)
        {
            if (!matchFlags[i]) {continue;}
            ms2[si] = max[i];
            si++;
        }
 
        var transpositions = ms1.Where((t, mi) => t != ms2[mi]).Count();
        var prefix = 0;
        for (var mi = 0; mi < min.Length; mi++)
        {
            if (s1[mi] == s2[mi])
            {
                prefix++;
            }
            else
            {
                break;
            }
        }
 
        return new [] { matches, transpositions / 2, prefix, max.Length };
    }
}
