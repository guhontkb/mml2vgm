using Corex64;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace mml2vgmIDEx64
{
    public class CompileManager
    {
        public enum enmCompileCommand
        {
            compile, play
        }

        public class queItem
        {
            public enmCompileCommand cmd;
            public Document doc;
            public string srcText;
            public object param;
            //private enmCompileCommand compile;
            public Action<queItem> callPlayBack;

            public queItem(enmCompileCommand compile, Document doc, string srcText, Action<queItem> callPlayBack = null, object param = null)
            {
                this.cmd = compile;
                this.doc = doc;
                this.srcText = srcText;
                this.callPlayBack = callPlayBack;
                this.param = param;
            }
        }

        private Queue<queItem> queCompile = new Queue<queItem>();
        private object lockobj = new object();
        private Thread _runner = null;
        public Thread runner
        {
            get
            {
                lock (lockobj) return _runner;
            }
            set
            {
                lock (lockobj) _runner = value;
            }
        }
        private Action<string> disp = null;
        private mucomManager mucom = null;
        private PMDManager pmd = null;
        private MoonDriverManager moonDriver = null;
        private MuapManager muap = null;

        public CompileManager(Action<string> disp, mucomManager mucom,PMDManager pmd, MoonDriverManager moonDriver, MuapManager muap)
        {
            this.disp = disp;
            this.mucom = mucom;
            this.pmd = pmd;
            this.moonDriver = moonDriver;
            this.muap = muap;
        }

        public void RequestCompile(Document doc, string srcText,object param=null)
        {
            if (doc == null) return;
            if (doc.compileStatus == Document.EnmCompileStatus.Compiling) return;

            Log.ForcedWrite($"Compile requested: {doc.gwiFullPath}");
            doc.compileStatus = Document.EnmCompileStatus.ReqCompile;
            queCompile.Enqueue(new queItem(enmCompileCommand.compile, doc, srcText, null, param));

            KickRunner();
        }

        public void RequestPlayBack(Document doc, Action<queItem> callPlayBack)
        {
            if (doc == null) return;
            queCompile.Enqueue(new queItem(enmCompileCommand.play, doc, null, callPlayBack, null));

            KickRunner();
        }


        private void KickRunner()
        {
            if (runner != null) return;

            runner = new Thread(run);
            runner.Start();
        }

        private void run()
        {
            try
            {
                while (queCompile.Count > 0)
                {
                    queItem qi = queCompile.Dequeue();
                    if (qi.doc == null) continue;

                    try
                    {
                        switch (qi.cmd)
                        {
                            case enmCompileCommand.compile:
                                qi.doc.compileStatus = Document.EnmCompileStatus.Compiling;
                                qi.doc.errBox = null;
                                qi.doc.wrnBox = null;
                                qi.doc.compiledData = null;
                                qi.doc.dstFileFormat = EnmFileFormat.unknown;
                                if (qi.srcText == null)
                                {
                                    qi.doc.compileStatus = Document.EnmCompileStatus.Success;//対象がない場合は無条件で成功
                                    break;
                                }
                                Log.ForcedWrite($"Compile started: {qi.doc.gwiFullPath}");
                                Compile(qi);
                                Log.ForcedWrite($"Compile finished: {qi.doc.gwiFullPath}, status={qi.doc.compileStatus}, format={qi.doc.dstFileFormat}, errors={qi.doc.errBox?.Length ?? 0}, warnings={qi.doc.wrnBox?.Length ?? 0}");
                                if (qi.doc.compileStatus == Document.EnmCompileStatus.Failed || (qi.doc.errBox?.Length ?? 0) > 0)
                                {
                                    foreach (msgInfo error in qi.doc.errBox ?? Array.Empty<msgInfo>())
                                        Log.ForcedWrite($"Compile error: {qi.doc.gwiFullPath}, {error.filename}:{(error.line < 0 ? "-" : (error.line + 1).ToString())}:{(error.column < 0 ? "-" : (error.column + 1).ToString())}: {error.body}");
                                }
                                break;
                            case enmCompileCommand.play:
                                if (qi.doc.compileStatus != Document.EnmCompileStatus.Success)
                                {
                                    Log.ForcedWrite($"Compile post-processing skipped: {qi.doc.gwiFullPath}, status={qi.doc.compileStatus}");
                                    break;
                                }
                                Log.ForcedWrite($"Compile post-processing started: {qi.doc.gwiFullPath}, format={qi.doc.dstFileFormat}");
                                qi.callPlayBack?.Invoke(qi);
                                Log.ForcedWrite($"Compile post-processing returned: {qi.doc.gwiFullPath}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.ForcedWrite($"Compile {qi.cmd} failed: {qi.doc.gwiFullPath}");
                        Log.ForcedWrite(ex);
                        qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                        qi.doc.compiledData = null;
                        qi.doc.dstFileFormat = EnmFileFormat.unknown;
                    }
                }
            }
            finally
            {
                runner = null;
            }
        }



        private void Compile(queItem qi)
        {

            string ext = Path.GetExtension(qi.doc.gwiFullPath).ToLower();
            switch (ext)
            {
                case ".gwi":
                    Compile_GWI(qi);
                    break;
                case ".muc":
                    Compile_MUC(qi);
                    break;
                case ".mml":
                    Compile_MML(qi);
                    break;
                case ".mdl":
                    Compile_MDL(qi);
                    break;
                case ".mus":
                    Compile_MUS(qi);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported compile source: {qi.doc.gwiFullPath}");
            }

        }

        private void Compile_MDL(queItem qi)
        {
            if (moonDriver == null)
            {
                Log.ForcedWrite($"MoonDriver compiler unavailable: {qi.doc.gwiFullPath}");
                qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                return;
            }

            string tempPath = Path.Combine(Common.GetApplicationDataFolder(true), "temp", Path.GetFileName(qi.doc.gwiFullPath));
            string path1 = Path.GetDirectoryName(qi.doc.gwiFullPath);
            path1 = string.IsNullOrEmpty(path1) ? qi.doc.gwiFullPath : path1;
            string wrkPath = path1;
            string compileOption = qi.param?.ToString() ?? string.Empty;
            msgBox.clear();
            musicDriverInterface.MmlDatum[] mData = null;
            bool isSuccess = true;

            try
            {
                Log.ForcedWrite($"MoonDriver compilation invoked: {qi.doc.gwiFullPath}, workPath={wrkPath}, option={compileOption}");
                mData = moonDriver.compileFromSrcText(qi.srcText, wrkPath, compileOption, Point.Empty, true);
            }
            catch (Exception ex)
            {
                Log.ForcedWrite($"MoonDriver compilation failed: {qi.doc.gwiFullPath}");
                Log.ForcedWrite(ex);
                isSuccess = false;
            }
            if (mData == null)
            {
                Log.ForcedWrite($"MoonDriver compilation returned null data: {qi.doc.gwiFullPath}");
                isSuccess = false;
            }

            qi.doc.compiledData = mData;
            qi.doc.compileStatus = isSuccess ? Document.EnmCompileStatus.Success : Document.EnmCompileStatus.Failed;
            qi.doc.dstFileFormat = EnmFileFormat.MDR;

            musicDriverInterface.CompilerInfo ci = moonDriver.GetCompilerInfo();

            List<msgInfo> lstMsgInfo = new List<msgInfo>();
            if (ci != null && ci.errorList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.errorList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.errBox = lstMsgInfo.ToArray();

            lstMsgInfo.Clear();
            if (ci != null && ci.warningList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.warningList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.wrnBox = lstMsgInfo.ToArray();

        }

        private void Compile_MML(queItem qi)
        {
            if (pmd == null)
            {
                Log.ForcedWrite($"PMD compiler unavailable: {qi.doc.gwiFullPath}");
                qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                return;
            }

            string tempPath = Path.Combine(Common.GetApplicationDataFolder(true), "temp", Path.GetFileName(qi.doc.gwiFullPath));
            string path1 = Path.GetDirectoryName(qi.doc.gwiFullPath);
            path1 = string.IsNullOrEmpty(path1) ? qi.doc.gwiFullPath : path1;
            string wrkPath = path1;
            string compileOption = qi.param?.ToString() ?? string.Empty;
            msgBox.clear();
            musicDriverInterface.MmlDatum[] mData = null;
            bool isSuccess = true;

            try
            {
                Log.ForcedWrite($"PMD compilation invoked: {qi.doc.gwiFullPath}, workPath={wrkPath}, option={compileOption}");
                mData = pmd.compileFromSrcText(qi.srcText, wrkPath, compileOption, Point.Empty, true);
            }
            catch (Exception ex)
            {
                Log.ForcedWrite($"PMD compilation failed: {qi.doc.gwiFullPath}");
                Log.ForcedWrite(ex);
                isSuccess = false;
            }
            if (mData == null)
            {
                Log.ForcedWrite($"PMD compilation returned null data: {qi.doc.gwiFullPath}");
                isSuccess = false;
            }

            qi.doc.compiledData = mData;
            qi.doc.compileStatus = isSuccess ? Document.EnmCompileStatus.Success : Document.EnmCompileStatus.Failed;
            qi.doc.dstFileFormat = EnmFileFormat.M;

            musicDriverInterface.CompilerInfo ci = pmd.GetCompilerInfo();

            List<msgInfo> lstMsgInfo = new List<msgInfo>();
            if (ci != null && ci.errorList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.errorList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.errBox = lstMsgInfo.ToArray();

            lstMsgInfo.Clear();
            if (ci != null && ci.warningList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.warningList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.wrnBox = lstMsgInfo.ToArray();

        }

        private void Compile_MUC(queItem qi)
        {
            if (mucom == null)
            {
                Log.ForcedWrite($"mucom compiler unavailable: {qi.doc.gwiFullPath}");
                qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                return;
            }

            string tempPath = Path.Combine(Common.GetApplicationDataFolder(true), "temp", Path.GetFileName(qi.doc.gwiFullPath));
            string path1 = Path.GetDirectoryName(qi.doc.gwiFullPath);
            path1 = string.IsNullOrEmpty(path1) ? qi.doc.gwiFullPath : path1;
            string wrkPath = path1;
            msgBox.clear();
            musicDriverInterface.MmlDatum[] mubData = null;
            bool isSuccess = true;

            try
            {
                Log.ForcedWrite($"mucom compilation invoked: {qi.doc.gwiFullPath}, workPath={wrkPath}");
                mubData = mucom.compileFromSrcText(qi.srcText, wrkPath, Point.Empty);
            }
            catch (Exception ex)
            {
                Log.ForcedWrite($"mucom compilation failed: {qi.doc.gwiFullPath}");
                Log.ForcedWrite(ex);
                isSuccess = false;
            }
            if (mubData == null)
            {
                Log.ForcedWrite($"mucom compilation returned null data: {qi.doc.gwiFullPath}");
                isSuccess = false;
            }

            qi.doc.compiledData = mubData;
            qi.doc.compileStatus = isSuccess ? Document.EnmCompileStatus.Success : Document.EnmCompileStatus.Failed;
            qi.doc.dstFileFormat = EnmFileFormat.MUB;

            musicDriverInterface.CompilerInfo ci = mucom.GetCompilerInfo();

            List<msgInfo> lstMsgInfo = new List<msgInfo>();
            if (ci != null && ci.errorList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.errorList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.errBox = lstMsgInfo.ToArray();

            lstMsgInfo.Clear();
            if (ci != null && ci.warningList != null)
            {
                foreach (Tuple<int, int, string> mes in ci.warningList)
                {
                    msgInfo mi = new msgInfo(null,"", mes.Item1, mes.Item2, 0, mes.Item3);
                    lstMsgInfo.Add(mi);
                }
            }
            qi.doc.wrnBox = lstMsgInfo.ToArray();

            Tuple<string,string>[] mucomTag = mucom.GetTagFromBuf(mubData, new myEncoding());
            qi.doc.tag = mucomTag;

        }

        private void Compile_GWI(queItem qi)
        {
            object compileTargetDocument = qi.doc.editor;
            string stPath = System.Windows.Forms.Application.StartupPath;
            string[] activeMMLTextLines = qi.srcText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string tempPath = Path.Combine(Common.GetApplicationDataFolder(true), "temp", Path.GetFileName(qi.doc.gwiFullPath));
            string path1 = Path.GetDirectoryName(qi.doc.gwiFullPath);
            path1 = string.IsNullOrEmpty(path1) ? qi.doc.gwiFullPath : path1;
            string wrkPath = path1;
            msgBox.clear();

            try
            {
                Log.ForcedWrite($"GWI compilation invoked: {qi.doc.gwiFullPath}, workPath={wrkPath}, tempPath={tempPath}, lines={activeMMLTextLines.Length}");

                Mml2vgm mv = new Mml2vgm(compileTargetDocument, activeMMLTextLines, tempPath, null, stPath, disp, wrkPath, false);
                mv.isIDE = true;
                int result = mv.Start();

                qi.doc.errBox = msgBox.getErr();
                qi.doc.wrnBox = msgBox.getWrn();

                disp("\r\n");
                disp(string.Format(Msg.get("I0110"), msgBox.getErr().Length, msgBox.getWrn().Length));
                if (mv.desVGM.loopSamples != -1)
                {
                    disp(string.Format(Msg.get("I0111"), mv.desVGM.loopClock));
                    if (mv.desVGM.info.format == enmFormat.VGM)
                        disp(string.Format(Msg.get("I0112"), mv.desVGM.loopSamples, mv.desVGM.loopSamples / 44100L));
                    else
                        disp(string.Format(Msg.get("I0112"), mv.desVGM.loopSamples, mv.desVGM.loopSamples / (mv.desVGM.info.xgmSamplesPerSecond)));
                }
                disp(string.Format(Msg.get("I0113"), mv.desVGM.lClock));
                if (mv.desVGM.info.format == enmFormat.VGM)
                    disp(string.Format(Msg.get("I0114"), mv.desVGM.dSample, mv.desVGM.dSample / 44100L));
                else
                    disp(string.Format(Msg.get("I0114"), mv.desVGM.dSample, mv.desVGM.dSample / (mv.desVGM.info.xgmSamplesPerSecond)));
                disp(Msg.get("I0126"));

                if (result == 0)
                {
                    if (mv.desVGM.info.format == enmFormat.VGM)
                    {
                        uint EOFOffset = Common.getLE32(mv.desBuf, 0x04) + (uint)mv.desVGM.dummyCmdCounter;
                        Common.SetLE32(mv.desBuf, 0x04, EOFOffset);

                        uint GD3Offset = Common.getLE32(mv.desBuf, 0x14) + (uint)mv.desVGM.dummyCmdCounter;
                        Common.SetLE32(mv.desBuf, 0x14, GD3Offset);

                        uint LoopOffset = (uint)mv.desVGM.dummyCmdLoopOffset - 0x1c;
                        Common.SetLE32(mv.desBuf, 0x1c, LoopOffset);
                    }

                    qi.doc.dstFileFormat = mv.desVGM.info.format == enmFormat.VGM
                        ? EnmFileFormat.VGM
                        : (mv.desVGM.info.format == enmFormat.XGM
                        ? EnmFileFormat.XGM
                        : EnmFileFormat.ZGM);
                    qi.doc.compiledData = mv;
                    qi.doc.compileStatus = Document.EnmCompileStatus.Success;
                }
                else
                {
                    Log.ForcedWrite($"GWI compilation failed with non-zero result: {qi.doc.gwiFullPath}, result={result}, errors={qi.doc.errBox?.Length ?? 0}, warnings={qi.doc.wrnBox?.Length ?? 0}");
                    qi.doc.dstFileFormat = EnmFileFormat.unknown;
                    qi.doc.compiledData = null;
                    qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                Log.ForcedWrite($"GWI compilation failed with exception: {qi.doc.gwiFullPath}");
                Log.ForcedWrite(ex);
                qi.doc.errBox = msgBox.getErr();
                qi.doc.wrnBox = msgBox.getWrn();
                qi.doc.dstFileFormat = EnmFileFormat.unknown;
                qi.doc.compiledData = null;
                qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
            }

        }

        private void Compile_MUS(queItem qi)
        {
            if (muap == null)
            {
                Log.ForcedWrite($"muap compiler unavailable: {qi.doc.gwiFullPath}");
                qi.doc.compileStatus = Document.EnmCompileStatus.Failed;
                return;
            }

            Log.ForcedWrite($"MUS compilation is not implemented: {qi.doc.gwiFullPath}");
            qi.doc.compileStatus = Document.EnmCompileStatus.Failed;

            //string tempPath = Path.Combine(Common.GetApplicationDataFolder(true), "temp", Path.GetFileName(qi.doc.gwiFullPath));
            //string path1 = Path.GetDirectoryName(qi.doc.gwiFullPath);
            //path1 = string.IsNullOrEmpty(path1) ? qi.doc.gwiFullPath : path1;
            //string wrkPath = path1;
            //msgBox.clear();
            //musicDriverInterface.MmlDatum[] mubData = null;
            //bool isSuccess = true;

            //try
            //{
            //    mubData = muap.compileFromSrcText(qi.srcText, wrkPath, Point.Empty);
            //}
            //catch
            //{
            //    isSuccess = false;
            //}
            //if (mubData == null) isSuccess = false;

            //qi.doc.compiledData = mubData;
            //qi.doc.compileStatus = isSuccess ? Document.EnmCompileStatus.Success : Document.EnmCompileStatus.Failed;
            //qi.doc.dstFileFormat = EnmFileFormat.MUB;

            //musicDriverInterface.CompilerInfo ci = mucom.GetCompilerInfo();

            //List<msgInfo> lstMsgInfo = new List<msgInfo>();
            //if (ci != null && ci.errorList != null)
            //{
            //    foreach (Tuple<int, int, string> mes in ci.errorList)
            //    {
            //        msgInfo mi = new msgInfo(null, "", mes.Item1, mes.Item2, 0, mes.Item3);
            //        lstMsgInfo.Add(mi);
            //    }
            //}
            //qi.doc.errBox = lstMsgInfo.ToArray();

            //lstMsgInfo.Clear();
            //if (ci != null && ci.warningList != null)
            //{
            //    foreach (Tuple<int, int, string> mes in ci.warningList)
            //    {
            //        msgInfo mi = new msgInfo(null, "", mes.Item1, mes.Item2, 0, mes.Item3);
            //        lstMsgInfo.Add(mi);
            //    }
            //}
            //qi.doc.wrnBox = lstMsgInfo.ToArray();

            //Tuple<string, string>[] mucomTag = mucom.GetTagFromBuf(mubData, new myEncoding());
            //qi.doc.tag = mucomTag;

        }

    }
}
