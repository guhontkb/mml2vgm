using musicDriverInterface;
using System;
using System.Collections.Generic;

namespace Corex64
{
    public static class msgBox
    {
        private static List<msgInfo> errBox = new List<msgInfo>();
        private static List<msgInfo> wrnBox = new List<msgInfo>();

        public static void clear()
        {
            errBox = new List<msgInfo>();
            wrnBox = new List<msgInfo>();
        }

        public static void setErrMsg(msgInfo msg)
        {
            addErrMsgIfNotExists(msg);
        }

        public static void setErrMsg(string msg, LinePos lp)
        {
            //errBox.Add(string.Format("(F : {0}  L : {1}) : {2}", System.IO.Path.GetFileName(fn), lineNumber, msg));
            if (lp != null)
            {
                addErrMsgIfNotExists(new msgInfo(lp.document,lp.srcMMLID, lp.row, lp.col, lp.length, msg));
            }
            else
            {
                addErrMsgIfNotExists(new msgInfo(null,"-", -1, -1, -1, msg));
            }
        }

        public static void setErrMsgOncePerFile(string msg, LinePos lp)
        {
            if (lp != null)
            {
                addErrMsgIfNotExists(new msgInfo(lp.document, lp.srcMMLID, lp.row, lp.col, lp.length, msg), true);
            }
            else
            {
                addErrMsgIfNotExists(new msgInfo(null, "-", -1, -1, -1, msg), true);
            }
        }

        private static void addErrMsgIfNotExists(msgInfo msg, bool ignoreLine = false)
        {
            foreach (msgInfo existing in errBox)
            {
                bool sameFile = msg.document != null
                    ? ReferenceEquals(existing.document, msg.document)
                    : existing.document == null && string.Equals(existing.filename, msg.filename, System.StringComparison.Ordinal);
                if (sameFile && (ignoreLine || existing.line == msg.line) && string.Equals(existing.body, msg.body, System.StringComparison.Ordinal)) return;
            }

            errBox.Add(msg);
        }

        public static void setWrnMsg(msgInfo msg)
        {
            wrnBox.Add(msg);
        }

        public static void setWrnMsg(string msg, LinePos lp)
        {
            if (lp != null)
            {
                wrnBox.Add(new msgInfo(lp.document, lp.srcMMLID, lp.row, lp.col, lp.length, msg));
            }
            else
            {
                wrnBox.Add(new msgInfo(null, "-", -1, -1, -1, msg));
            }
        }

        public static msgInfo[] getErr()
        {
            return getSortedMessages(errBox);
        }

        public static msgInfo[] getWrn()
        {
            return getSortedMessages(wrnBox);
        }

        private static msgInfo[] getSortedMessages(List<msgInfo> messages)
        {
            msgInfo[] sorted = messages.ToArray();
            Array.Sort(sorted, CompareMessagesByFileAndLine);
            return sorted;
        }

        private static int CompareMessagesByFileAndLine(msgInfo first, msgInfo second)
        {
            int comparison = StringComparer.OrdinalIgnoreCase.Compare(first.filename, second.filename);
            if (comparison != 0) return comparison;

            bool firstHasLine = first.line >= 0;
            bool secondHasLine = second.line >= 0;
            if (firstHasLine && secondHasLine)
            {
                comparison = first.line.CompareTo(second.line);
                if (comparison != 0) return comparison;
            }
            else if (firstHasLine)
            {
                return -1;
            }
            else if (secondHasLine)
            {
                return 1;
            }

            comparison = first.column.CompareTo(second.column);
            if (comparison != 0) return comparison;

            return StringComparer.Ordinal.Compare(first.body, second.body);
        }
    }

    public class msgInfo
    {
        public object document = null;
        public string filename = "-";
        public int line = -1;
        public int column = -1;
        public int length = -1;
        public string body = "";

        public msgInfo(object document,string filename, int line, int column, int length, string body)
        {
            this.document = document;
            this.filename = filename;
            this.line = line;
            this.column = column;
            this.length = length;
            this.body = body;
        }
    }
}
