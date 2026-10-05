using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XHelper
{
    public struct RelativePath
    {
        private string m_value;
        public string Value
        {
            get
            {
                return m_value;
            }
        }

        public bool IsEmpty
        {
            get
            {
                return StringUtils.IsEmpty(m_value);
            }
        }

        public RelativePath(string value)
        {
            m_value = value;
        }

        public override string ToString()
        {
            return m_value == null ? "" : m_value;
        }

        public RelativePath FileToDirectoryPath
        {
            get
            {
                return new RelativePath(Path.GetDirectoryName(m_value));
            }
        }

        public static RelativePath operator+ (RelativePath lhs, RelativePath rhs)
        {
            return new RelativePath(Path.Combine(lhs.Value, rhs.Value));
        }
    }

    public struct AbsolutePath
    {
        private string m_value;
        public string Value
        {
            get
            {
                return m_value;
            }
        }

        public bool IsEmpty
        {
            get
            {
                return StringUtils.IsEmpty(m_value);
            }
        }

        public AbsolutePath(string value)
        {
            m_value = Path.GetFullPath(value);
        }

        public override string ToString()
        {
            return m_value == null ? "" : m_value;
        }

        public override bool Equals(object obj)
        {
            return obj is AbsolutePath path &&
                   m_value == path.m_value;
        }

        public override int GetHashCode()
        {
            if (m_value == null)
            {
                return 0;
            }
            return m_value.GetHashCode();
        }

        public AbsolutePath Combines(params RelativePath[] paths)
        {
            AbsolutePath result = this;
            foreach (var item in paths)
            {
                result = result + item;
            }

            return result;
        }

        public AbsolutePath? ResolveTo(bool isFile)
        {
            return ResolvePath(this, isFile);
        }

        public AbsolutePath FileToDirectoryPath
        {
            get
            {
                return new AbsolutePath(Path.GetDirectoryName(m_value));
            }
        }

        public static RelativePath operator- (AbsolutePath lhs, AbsolutePath rhs)
        {
            if (lhs.IsEmpty || rhs.IsEmpty)
            {
                return default;
            }
            return new RelativePath(Path.GetRelativePath(rhs.Value, lhs.Value));
        }

        public static AbsolutePath operator +(AbsolutePath lhs, RelativePath rhs)
        {
            if (lhs.IsEmpty || rhs.IsEmpty)
            {
                return lhs;
            }
            return new AbsolutePath(Path.Combine(lhs.Value, rhs.Value));
        }

        public static bool operator ==(AbsolutePath lhs, AbsolutePath rhs)
        {
            return lhs.Value == rhs.Value;
        }

        public static bool operator !=(AbsolutePath lhs, AbsolutePath rhs)
        {
            return lhs.Value != rhs.Value;
        }

        public static AbsolutePath? ResolvePath(AbsolutePath path, bool isFile)
        {
            if (path.IsEmpty)
            {
                return null;
            }

            List<string> parts = SplitPathToParts(path.Value);
            if (parts.Count == 0)
            {
                return null;
            }

            if (parts[0].Contains("*"))
            {
                throw new Exception($"Cannot search in base directoies: {path}");
            }

            List<string> results = new List<string>();
            ResolvePathWithWildcard_Recusive(results, null, parts, 0, isFile);

            if (results.Count == 0)
            {
                return null;
            }
            else
            {
                return new AbsolutePath(results[0]);
            }
        }

        private static void ResolvePathWithWildcard_Recusive(List<string> results, string root, List<string> restPath, int restIndex, bool isFile)
        {
            bool isFinal = restIndex == restPath.Count - 1;
            string patther = restPath[restIndex];
            string newPath;
            if (root == null)
            {
                newPath = patther;
            }
            else
            {
                newPath = Path.Combine(root, patther);
            }
            bool isPattern = patther.Contains("*");
            if (isFinal)
            {
                if (isFile)
                {
                    if (isPattern)
                    {
                        foreach (var file in Directory.EnumerateFiles(root, patther, SearchOption.TopDirectoryOnly))
                        {
                            if (File.Exists(file))
                            {
                                results.Add(file);
                            }
                        }
                    }
                    else
                    {
                        if (File.Exists(newPath))
                        {
                            results.Add(newPath);
                        }
                    }
                }
                else
                {
                    if (isPattern)
                    {
                        foreach (var dir in Directory.EnumerateDirectories(root, patther, SearchOption.TopDirectoryOnly))
                        {
                            if (Directory.Exists(dir))
                            {
                                results.Add(dir);
                            }
                        }
                    }
                    else
                    {
                        if (Directory.Exists(newPath))
                        {
                            results.Add(newPath);
                        }
                    }
                }
            }
            else
            {
                if (isPattern)
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, patther, SearchOption.TopDirectoryOnly))
                    {
                        ResolvePathWithWildcard_Recusive(results, dir, restPath, restIndex + 1, isFile);
                    }
                }
                else
                {
                    ResolvePathWithWildcard_Recusive(results, newPath, restPath, restIndex + 1, isFile);
                }
            }
        }

        private static List<string> SplitPathToParts(string path)
        {
            List<string> result = new List<string>();
            string[] parts = path.Split(Path.DirectorySeparatorChar);
            if (path.StartsWith(Path.DirectorySeparatorChar))
            {
                parts[0] = $"{Path.DirectorySeparatorChar}{parts[0]}";
            }

            string curResult = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Contains("*"))
                {
                    if (curResult != null)
                    {
                        result.Add(curResult);
                        curResult = null;
                    }
                    result.Add(part);
                }
                else
                {
                    if (curResult == null)
                    {
                        curResult = part;
                    }
                    else
                    {
                        curResult = Path.Combine(curResult, part);
                    }
                    if (i == parts.Length - 1)
                    {
                        result.Add(curResult);
                    }
                }
            }

            return result;
        }
    }

    public static class SystemUtils
    {

    }
}

