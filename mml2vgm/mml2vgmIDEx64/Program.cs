using System.Diagnostics;
using System.Runtime.InteropServices;

namespace mml2vgmIDEx64
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //DOBON.NET ����̃R�[�h�𗬗p

            string mutexName = "mml2vgmIDE";
            bool createdNew;

            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                //�~���[�e�b�N�X�̏������L�����t�^���ꂽ�����ׂ�
                if (!createdNew)
                {
                    if (Environment.GetCommandLineArgs().Length > 1)
                    {
                        Process prc = GetPreviousProcess();
                        if (prc != null)
                        {
                            SendString(prc.MainWindowHandle, Environment.GetCommandLineArgs()[1]);
                            return;
                        }
                    }

                    //����Ȃ������ꍇ�́A���łɋN�����Ă���Ɣ��f���ďI��
                    MessageBox.Show("二重起動はできません。");
                    return;
                }

                try
                {

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException +=
                        new System.Threading.ThreadExceptionEventHandler(
                            Application_ThreadException);

                    Setting setting = Setting.Load();
                    Audio.Init(setting);

                    FrmMain f = new FrmMain
                    {
                        setting = setting
                    };
                    f.Init();

                    Application.Run(f);

                }
                catch (Exception ex)
                {
                    try
                    {
                        Log.ForcedWrite(ex);
                        MessageBox.Show(ex.Message, "�v���I�ȃG���[");
                    }
                    finally
                    {
                    }
                }
                finally
                {
                    Log.Shutdown();
                    //�~���[�e�b�N�X���������
                    mutex.ReleaseMutex();
                }

            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                Log.ForcedWrite(e.Exception);
                MessageBox.Show(e.Exception.Message, "�v���I�ȃG���[");
            }
            finally
            {
                //�A�v���P�[�V�������I������
                Application.Exit();
            }
        }

        public static Process GetPreviousProcess()
        {
            Process curProcess = Process.GetCurrentProcess();
            Process[] allProcesses = Process.GetProcessesByName(curProcess.ProcessName);

            foreach (Process checkProcess in allProcesses)
            {
                // �������g�̃v���Z�XID�͖�������
                if (checkProcess.Id != curProcess.Id)
                {
                    // �v���Z�X�̃t���p�X�����r���ē����A�v���P�[�V����������
                    if (string.Compare(
                        checkProcess.MainModule.FileName,
                        curProcess.MainModule.FileName, true) == 0)
                    {
                        // �����t���p�X���̃v���Z�X���擾
                        return checkProcess;
                    }
                }
            }

            // �����A�v���P�[�V�����̃v���Z�X��������Ȃ��I
            return null;
        }


        //SendMessage�ő���\���́iUnicode�����񑗐M�ɍœK�������p�^�[���j
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpData;
        }

        //SendMessage�i�f�[�^�]���j
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
        public const int WM_COPYDATA = 0x004A;
        public const int WM_PASTE = 0x0302;

        //SendMessage���g���ăv���Z�X�ԒʐM�ŕ������n��
        public static void SendString(IntPtr targetWindowHandle, string str)
        {
            COPYDATASTRUCT cds = new COPYDATASTRUCT();
            cds.dwData = IntPtr.Zero;
            cds.lpData = str;
            cds.cbData = str.Length * sizeof(char);
            //��M���ł�lpData�̕������(cbData/2)�̒�����string.Substring()����

            IntPtr myWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
            SendMessage(targetWindowHandle, WM_COPYDATA, myWindowHandle, ref cds);
        }

    }
}