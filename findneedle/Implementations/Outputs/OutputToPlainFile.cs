using findneedle.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations
{
    public class OutputToPlainFile : SearchOutput
    {
        string filename = "";
        FileStream x;
        public OutputToPlainFile(string filename = "")
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                filename = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".txt";

            }
            x = File.OpenWrite(filename);
            this.filename = filename;
        }

        ~OutputToPlainFile()
        {
            x.Close();
        }
        public void WriteAllOutput(List<SearchResult> result)
        {
            foreach(SearchResult item in result)
            {
                WriteOutput(item);
            }
        }

        public void WriteOutput(SearchResult result)
        {
            byte[] info = new UTF8Encoding(true).GetBytes(result.GetMessage());
            x.Write(info);
        }

        public string GetOutputFileName()
        {
            return filename;
        }

        public string GetTextDescription() => throw new NotImplementedException();
        public string GetFriendlyName() => throw new NotImplementedException();
        public string GetClassName() => throw new NotImplementedException();
    }
}
