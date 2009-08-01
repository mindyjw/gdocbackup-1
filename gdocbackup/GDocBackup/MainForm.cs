/*
   Copyright 2009  Fabrizio Accatino

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Net;
using Google.Documents;
using GDocBackupLib;


namespace GDocBackup
{
    public partial class MainForm : Form
    {

        private Thread _workingThread;


        /// <summary>
        /// Log data
        /// </summary>
        private List<string> _logs = null;


        /// <summary>
        /// Autostart flag
        /// </summary>
        public bool AutoStart = false;


        /// <summary>
        /// Autostart flag
        /// </summary>
        public bool WriteLog = false;


        /// <summary>
        /// [Constructor]
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }


        /// <summary>
        /// Form load event
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Icon = Properties.Resources.Logo;
            this.Text += " - Ver. " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.StoreLogMsg(-1, "FORM_LOAD: " + this.Text);

            if (Properties.Settings.Default.CallUpgrade)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.CallUpgrade = false;
                Properties.Settings.Default.Save();
            }

            this.ExecCheckUpdates();

            if (String.IsNullOrEmpty(Properties.Settings.Default.BackupDir))
            {
                using (ConfigForm cf = new ConfigForm())
                {
                    cf.ShowDialog();
                }
            }
        }


        /// <summary>
        /// Form Shown event
        /// </summary>
        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (AutoStart)
                this.ExecBackUp();
        }


        /// <summary>
        /// Check if an update is available. If yes, show a Message Form.
        /// </summary>
        private void ExecCheckUpdates()
        {
            if (!Properties.Settings.Default.DisableUpdateCheck)
            {
                if (DateTime.Now.Subtract(Properties.Settings.Default.LastUpdateCheck).TotalDays > 6)
                {
                    Version localVersion;
                    Version remoteVersion;
                    if (CheckUpdates.Exec(out localVersion, out remoteVersion))
                    {
                        using (NewVersion nv = new NewVersion())
                        {
                            nv.LocalVersion = localVersion;
                            nv.RemoteVersion = remoteVersion;
                            nv.ShowDialog();
                        }
                    }
                    Properties.Settings.Default.LastUpdateCheck = DateTime.Now;
                    Properties.Settings.Default.Save();
                }
            }
        }


        /// <summary>
        /// Add log message to list and file
        /// </summary>
        /// <param name="s"></param>
        private void StoreLogMsg(int percent, string s)
        {
            string msg = DateTime.Now.ToString() + " - " + percent.ToString("000") + " > " + s;
            if (_logs != null)
                _logs.Add(msg);
            if (WriteLog)
            {
                string logFileName = "GDocBackup_" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                string logFileNameFP = Path.Combine(Path.GetTempPath(), logFileName);
                File.AppendAllText(logFileNameFP, msg + Environment.NewLine);
            }
        }


        #region  ---- User click on Buttons, Menu items & C.  ----

        private void BtnExec_Click(object sender, EventArgs e)
        {
            if (this.BtnExec.Text != "STOP")
                this.ExecBackUp();
            else
                if (_workingThread != null && _workingThread.IsAlive)
                    _workingThread.Abort();
        }

        private void configToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (ConfigForm cf = new ConfigForm())
            {
                cf.ShowDialog();
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (AboutForm af = new AboutForm())
                af.ShowDialog();
        }

        private void logsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (LogsForm lf = new LogsForm())
            {
                if (_logs != null)
                    lf.Logs = _logs.ToArray();
                lf.ShowDialog();
            }
        }

        #endregion --------------------------------



        #region ---- Backup thread ----

        /// <summary>
        /// Start backup in a separate (background) thread
        /// </summary>
        private void ExecBackUp()
        {
            string userName = Properties.Settings.Default.UserName;
            string password =
                String.IsNullOrEmpty(Properties.Settings.Default.Password) ?
                String.Empty :
                Utility.UnprotectData(Properties.Settings.Default.Password);
            string backupDir = Properties.Settings.Default.BackupDir;

            if (String.IsNullOrEmpty(userName) || String.IsNullOrEmpty(password) || String.IsNullOrEmpty(backupDir))
            {
                LoginForm login = new LoginForm();
                login.UserName = userName;
                login.BackupDir = backupDir;
                if (login.ShowDialog() == DialogResult.OK)
                {
                    userName = login.UserName;
                    password = login.Password;
                    backupDir = login.BackupDir;
                }
                else
                    return;
            }

            _logs = new List<string>();
            this.dataGV.Rows.Clear();
            this.BtnExec.Text = "STOP";
            this.Cursor = Cursors.WaitCursor;
            this.dataGV.Cursor = Cursors.WaitCursor;   // Need to set it (perhaps a bug of DataGridView...)

            // Start working thread
            _workingThread = new Thread(ExecBackupThread);
            _workingThread.IsBackground = true;
            _workingThread.Name = "BackupExecThread";
            _workingThread.Start(new string[] { userName, password, backupDir });
        }


        /// <summary>
        /// Exec backup (new thread "body")
        /// </summary>
        private void ExecBackupThread(object data)
        {
            String[] parameters = data as String[];

            Properties.Settings conf = Properties.Settings.Default;

            IWebProxy webproxy =  Utility.GetProxy(conf.ProxyExplicit, conf.ProxyDirectConnection, conf.ProxyHostPortSource, conf.ProxyHost, conf.ProxyPort, conf.ProxyAuthMode, conf.ProxyUsername, conf.ProxyPassword);

            Backup b = new Backup(
                parameters[0],
                parameters[1],
                parameters[2],
                Utility.DecodeDownloadTypeArray(conf.DocumentExportFormat).ToArray(),
                Utility.DecodeDownloadTypeArray(conf.SpreadsheetExportFormat).ToArray(),
                Utility.DecodeDownloadTypeArray(conf.PresentationExportFormat).ToArray(),
                webproxy);

            b.Feedback += new EventHandler<FeedbackEventArgs>(Backup_Feedback);
            bool result = b.Exec();
            this.BeginInvoke((MethodInvoker)delegate() { EndDownload(result, b.LastException); });
        }


        /// <summary>
        /// Backup feedback event handler
        /// </summary>
        private void Backup_Feedback(object sender, FeedbackEventArgs e)
        {
            if (this.InvokeRequired)
                this.BeginInvoke(new EventHandler<FeedbackEventArgs>(Backup_Feedback), sender, e);
            else
            {
                int percent = (int)(e.PerCent * 100);

                this.progressBar1.Value = percent;

                if (!String.IsNullOrEmpty(e.Message))
                    this.StoreLogMsg(percent, e.Message);

                if (e.FeedbackObj != null)
                {
                    this.StoreLogMsg(percent, "FeedbackObj: " + e.FeedbackObj.ToString());
                    this.dataGV.Rows.Add(
                        new object[] { 
                            e.FeedbackObj.FileName, 
                            e.FeedbackObj.DocType, 
                            e.FeedbackObj.ExportFormat.ToUpper(),
                            e.FeedbackObj.Action,
                            e.FeedbackObj.RemoteDateTime.ToString() });
                }
            }
        }


        /// <summary>
        /// End download event handler
        /// </summary>
        private void EndDownload(bool isOK, Exception ex)
        {
            this.progressBar1.Value = 0;
            this.Cursor = Cursors.Default;
            this.dataGV.Cursor = Cursors.Default;    // Need to set it (perhaps a bug of DataGridView...)
            this.BtnExec.Enabled = true;
            this.BtnExec.Text = "Exec";

            if (isOK)
            {
                if (AutoStart)
                {
                    Application.Exit();
                    return;   // Win does not requires it. Mono on Ubuntu, yes!  Is a Mono bug???   ... TO BE MORE TESTED  :(
                }
                else
                    MessageBox.Show("Backup completed.", "GDocBackup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                string msg = "Backup completed." + Environment.NewLine + Environment.NewLine +
                   "There are errors! " + Environment.NewLine + Environment.NewLine;
                if (ex != null)
                {
                    this.StoreLogMsg(-1, "############### EXCEPTION ###############");
                    this.StoreLogMsg(-1, ex.ToString());
                    this.StoreLogMsg(-1, "#########################################");
                    msg += ex.GetType().Name + " : " + ex.Message;
                }
                MessageBox.Show(msg, "GDocBackup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion ----------------------------

    }
}