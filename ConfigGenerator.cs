/*
 * P2P 配置生成器 - 主窗口
 * 功能：可视化生成服务器和客户端配置文件，SQLite 记录历史配置
 */

using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace P2PConfigGenerator
{
    public class MainForm : Form
    {
        private SQLiteConnection dbConnection;
        private string dbPath = "p2p_configs.db";

        // UI 控件
        private TabControl tabControl;
        private ComboBox cmbServiceType;
        private TextBox txtGroupID;
        private TextBox txtGroupKey;
        private TextBox txtGroupDesc;
        private TextBox txtServerIP1;
        private TextBox txtServerIP2;
        private NumericUpDown numServerPort;
        private NumericUpDown numLocalPort;
        private NumericUpDown numTargetPort;
        private TextBox txtProviderPeerID;
        private TextBox txtConsumerPeerID;
        private DataGridView dgvHistory;
        private Button btnGenerate;
        private Button btnExport;
        private Button btnLoadHistory;

        public MainForm()
        {
            InitializeComponent();
            InitializeDatabase();
            LoadHistory();
        }

        private void InitializeComponent()
        {
            this.Text = "P2P 配置生成器 v1.0";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 主选项卡
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // 配置页面
            var tabConfig = new TabPage("生成配置");
            CreateConfigTab(tabConfig);
            tabControl.TabPages.Add(tabConfig);

            // 历史页面
            var tabHistory = new TabPage("配置历史");
            CreateHistoryTab(tabHistory);
            tabControl.TabPages.Add(tabHistory);

            this.Controls.Add(tabControl);
        }

        private void CreateConfigTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            int y = 10;

            // 服务类型
            panel.Controls.Add(new Label { Text = "服务类型:", Location = new Point(10, y), AutoSize = true });
            cmbServiceType = new ComboBox
            {
                Location = new Point(150, y),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbServiceType.Items.AddRange(new[] { "SQL Server (1433)", "远程桌面 (3389)", "自定义端口" });
            cmbServiceType.SelectedIndex = 0;
            cmbServiceType.SelectedIndexChanged += ServiceType_Changed;
            panel.Controls.Add(cmbServiceType);
            y += 35;

            // 组ID
            panel.Controls.Add(new Label { Text = "组ID (GroupID):", Location = new Point(10, y), AutoSize = true });
            txtGroupID = new TextBox { Location = new Point(150, y), Width = 200 };
            panel.Controls.Add(txtGroupID);
            panel.Controls.Add(new Button
            {
                Text = "自动生成",
                Location = new Point(360, y - 2),
                Width = 80,
                Height = 24
            }.Also(b => b.Click += (s, e) => txtGroupID.Text = $"group_{Guid.NewGuid().ToString().Substring(0, 8)}"));
            y += 35;

            // 组密钥
            panel.Controls.Add(new Label { Text = "组密钥 (GroupKey):", Location = new Point(10, y), AutoSize = true });
            txtGroupKey = new TextBox { Location = new Point(150, y), Width = 200 };
            panel.Controls.Add(txtGroupKey);
            panel.Controls.Add(new Button
            {
                Text = "生成随机密钥",
                Location = new Point(360, y - 2),
                Width = 100,
                Height = 24
            }.Also(b => b.Click += (s, e) => txtGroupKey.Text = GenerateRandomKey()));
            y += 35;

            // 组描述
            panel.Controls.Add(new Label { Text = "组描述:", Location = new Point(10, y), AutoSize = true });
            txtGroupDesc = new TextBox { Location = new Point(150, y), Width = 400 };
            panel.Controls.Add(txtGroupDesc);
            y += 35;

            // 分隔线
            panel.Controls.Add(new Label
            {
                Text = "══════════════════════ 服务器配置 ══════════════════════",
                Location = new Point(10, y),
                Width = 700,
                ForeColor = Color.Blue
            });
            y += 30;

            // 服务器IP1
            panel.Controls.Add(new Label { Text = "主服务器 IP:", Location = new Point(10, y), AutoSize = true });
            txtServerIP1 = new TextBox { Location = new Point(150, y), Width = 200, Text = "127.0.0.1" };
            panel.Controls.Add(txtServerIP1);
            y += 35;

            // 服务器IP2
            panel.Controls.Add(new Label { Text = "备用服务器 IP:", Location = new Point(10, y), AutoSize = true });
            txtServerIP2 = new TextBox { Location = new Point(150, y), Width = 200 };
            panel.Controls.Add(txtServerIP2);
            y += 35;

            // 服务器端口
            panel.Controls.Add(new Label { Text = "服务器端口:", Location = new Point(10, y), AutoSize = true });
            numServerPort = new NumericUpDown
            {
                Location = new Point(150, y),
                Width = 100,
                Minimum = 1000,
                Maximum = 65535,
                Value = 8000
            };
            panel.Controls.Add(numServerPort);
            y += 35;

            // 分隔线
            panel.Controls.Add(new Label
            {
                Text = "══════════════════════ 端口转发配置 ══════════════════════",
                Location = new Point(10, y),
                Width = 700,
                ForeColor = Color.Blue
            });
            y += 30;

            // 本地端口
            panel.Controls.Add(new Label { Text = "本地监听端口:", Location = new Point(10, y), AutoSize = true });
            numLocalPort = new NumericUpDown
            {
                Location = new Point(150, y),
                Width = 100,
                Minimum = 1,
                Maximum = 65535,
                Value = 1433
            };
            panel.Controls.Add(numLocalPort);
            y += 35;

            // 目标端口
            panel.Controls.Add(new Label { Text = "目标服务端口:", Location = new Point(10, y), AutoSize = true });
            numTargetPort = new NumericUpDown
            {
                Location = new Point(150, y),
                Width = 100,
                Minimum = 1,
                Maximum = 65535,
                Value = 1433
            };
            panel.Controls.Add(numTargetPort);
            y += 35;

            // 分隔线
            panel.Controls.Add(new Label
            {
                Text = "══════════════════════ 节点ID配置 ══════════════════════",
                Location = new Point(10, y),
                Width = 700,
                ForeColor = Color.Blue
            });
            y += 30;

            // 提供者PeerID
            panel.Controls.Add(new Label { Text = "提供服务节点ID:", Location = new Point(10, y), AutoSize = true });
            txtProviderPeerID = new TextBox { Location = new Point(150, y), Width = 200 };
            panel.Controls.Add(txtProviderPeerID);
            y += 35;

            // 消费者PeerID
            panel.Controls.Add(new Label { Text = "访问服务节点ID:", Location = new Point(10, y), AutoSize = true });
            txtConsumerPeerID = new TextBox { Location = new Point(150, y), Width = 200 };
            panel.Controls.Add(txtConsumerPeerID);
            y += 35;

            // 按钮
            btnGenerate = new Button
            {
                Text = "生成配置文件",
                Location = new Point(150, y + 20),
                Width = 150,
                Height = 40,
                Font = new Font("Microsoft YaHei", 10, FontStyle.Bold)
            };
            btnGenerate.Click += BtnGenerate_Click;
            panel.Controls.Add(btnGenerate);

            btnExport = new Button
            {
                Text = "导出配置包",
                Location = new Point(320, y + 20),
                Width = 150,
                Height = 40,
                Font = new Font("Microsoft YaHei", 10, FontStyle.Bold)
            };
            btnExport.Click += BtnExport_Click;
            panel.Controls.Add(btnExport);

            tab.Controls.Add(panel);
        }

        private void CreateHistoryTab(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            dgvHistory = new DataGridView
            {
                Location = new Point(10, 10),
                Size = new Size(850, 500),
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            panel.Controls.Add(dgvHistory);

            btnLoadHistory = new Button
            {
                Text = "加载选中配置",
                Location = new Point(10, 520),
                Width = 150,
                Height = 35
            };
            btnLoadHistory.Click += BtnLoadHistory_Click;
            panel.Controls.Add(btnLoadHistory);

            tab.Controls.Add(panel);
        }

        private void InitializeDatabase()
        {
            dbConnection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            dbConnection.Open();

            string createTable = @"
                CREATE TABLE IF NOT EXISTS configs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    group_id TEXT,
                    group_key TEXT,
                    group_desc TEXT,
                    service_type TEXT,
                    server_ip1 TEXT,
                    server_ip2 TEXT,
                    server_port INTEGER,
                    local_port INTEGER,
                    target_port INTEGER,
                    provider_peer_id TEXT,
                    consumer_peer_id TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            using (var cmd = new SQLiteCommand(createTable, dbConnection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void ServiceType_Changed(object sender, EventArgs e)
        {
            switch (cmbServiceType.SelectedIndex)
            {
                case 0: // SQL Server
                    numLocalPort.Value = 1433;
                    numTargetPort.Value = 1433;
                    txtGroupDesc.Text = "SQL Server 访问组";
                    break;
                case 1: // 远程桌面
                    numLocalPort.Value = 3389;
                    numTargetPort.Value = 3389;
                    txtGroupDesc.Text = "远程桌面访问组";
                    break;
            }
        }

        private string GenerateRandomKey()
        {
            return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            try
            {
                // 验证输入
                if (string.IsNullOrWhiteSpace(txtGroupID.Text) ||
                    string.IsNullOrWhiteSpace(txtGroupKey.Text) ||
                    string.IsNullOrWhiteSpace(txtProviderPeerID.Text) ||
                    string.IsNullOrWhiteSpace(txtConsumerPeerID.Text))
                {
                    MessageBox.Show("请填写所有必填项！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 保存到数据库
                SaveToDatabase();

                // 生成配置文件
                GenerateConfigFiles();

                MessageBox.Show("配置文件生成成功！\n\n已生成:\n- server_config.json (服务器配置)\n- client_config_提供服务.json\n- client_config_访问服务.json",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                LoadHistory();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveToDatabase()
        {
            string sql = @"INSERT INTO configs 
                (group_id, group_key, group_desc, service_type, server_ip1, server_ip2, 
                 server_port, local_port, target_port, provider_peer_id, consumer_peer_id)
                VALUES (@gid, @gkey, @gdesc, @stype, @ip1, @ip2, @sport, @lport, @tport, @ppid, @cpid)";

            using (var cmd = new SQLiteCommand(sql, dbConnection))
            {
                cmd.Parameters.AddWithValue("@gid", txtGroupID.Text);
                cmd.Parameters.AddWithValue("@gkey", txtGroupKey.Text);
                cmd.Parameters.AddWithValue("@gdesc", txtGroupDesc.Text);
                cmd.Parameters.AddWithValue("@stype", cmbServiceType.Text);
                cmd.Parameters.AddWithValue("@ip1", txtServerIP1.Text);
                cmd.Parameters.AddWithValue("@ip2", txtServerIP2.Text ?? "");
                cmd.Parameters.AddWithValue("@sport", (int)numServerPort.Value);
                cmd.Parameters.AddWithValue("@lport", (int)numLocalPort.Value);
                cmd.Parameters.AddWithValue("@tport", (int)numTargetPort.Value);
                cmd.Parameters.AddWithValue("@ppid", txtProviderPeerID.Text);
                cmd.Parameters.AddWithValue("@cpid", txtConsumerPeerID.Text);
                cmd.ExecuteNonQuery();
            }
        }

        private void GenerateConfigFiles()
        {
            var servers = new System.Collections.Generic.List<string> { txtServerIP1.Text };
            if (!string.IsNullOrWhiteSpace(txtServerIP2.Text))
                servers.Add(txtServerIP2.Text);

            // 生成服务器配置
            var serverConfig = new
            {
                ServerPort = (int)numServerPort.Value,
                MaxClients = 1000,
                Groups = new[]
                {
                    new
                    {
                        GroupID = txtGroupID.Text,
                        GroupKey = txtGroupKey.Text,
                        Description = txtGroupDesc.Text
                    }
                },
                Logging = new
                {
                    Level = "INFO",
                    LogToFile = true,
                    LogFilePath = "logs/server_{date}.log"
                },
                Advanced = new
                {
                    ClientTimeout = 30,
                    CleanupInterval = 10,
                    EnablePortForward = true
                }
            };

            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            File.WriteAllText("server_config.json",
                JsonSerializer.Serialize(serverConfig, jsonOptions));

            // 生成提供服务客户端配置
            var providerConfig = new
            {
                PeerID = txtProviderPeerID.Text,
                GroupID = txtGroupID.Text,
                GroupKey = txtGroupKey.Text,
                Servers = servers,
                ServerPort = (int)numServerPort.Value,
                PortForwards = new object[] { },
                Logging = new
                {
                    Level = "INFO",
                    LogToFile = true,
                    LogFilePath = $"logs/{txtProviderPeerID.Text}_{{date}}.log"
                },
                Advanced = new
                {
                    HeartbeatInterval = 1000,
                    PunchRetryCount = 10,
                    EnableP2P = true,
                    EnableRelay = true
                }
            };

            File.WriteAllText("client_config_提供服务.json",
                JsonSerializer.Serialize(providerConfig, jsonOptions));

            // 生成访问服务客户端配置
            var consumerConfig = new
            {
                PeerID = txtConsumerPeerID.Text,
                GroupID = txtGroupID.Text,
                GroupKey = txtGroupKey.Text,
                Servers = servers,
                ServerPort = (int)numServerPort.Value,
                PortForwards = new[]
                {
                    new
                    {
                        Name = txtGroupDesc.Text,
                        LocalPort = (int)numLocalPort.Value,
                        TargetPeerID = txtProviderPeerID.Text,
                        TargetPort = (int)numTargetPort.Value,
                        Protocol = "TCP"
                    }
                },
                Logging = new
                {
                    Level = "INFO",
                    LogToFile = true,
                    LogFilePath = $"logs/{txtConsumerPeerID.Text}_{{date}}.log"
                },
                Advanced = new
                {
                    HeartbeatInterval = 1000,
                    PunchRetryCount = 10,
                    EnableP2P = true,
                    EnableRelay = true
                }
            };

            File.WriteAllText("client_config_访问服务.json",
                JsonSerializer.Serialize(consumerConfig, jsonOptions));
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                var folderDialog = new FolderBrowserDialog { Description = "选择导出目录" };
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string exportPath = Path.Combine(folderDialog.SelectedPath, $"P2P配置_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(exportPath);

                    File.Copy("server_config.json", Path.Combine(exportPath, "server_config.json"), true);
                    File.Copy("client_config_提供服务.json", Path.Combine(exportPath, "client_config_提供服务.json"), true);
                    File.Copy("client_config_访问服务.json", Path.Combine(exportPath, "client_config_访问服务.json"), true);

                    MessageBox.Show($"配置包已导出到:\n{exportPath}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadHistory()
        {
            string sql = "SELECT id, group_id, group_desc, service_type, provider_peer_id, consumer_peer_id, created_at FROM configs ORDER BY id DESC";
            var adapter = new SQLiteDataAdapter(sql, dbConnection);
            var table = new DataTable();
            adapter.Fill(table);

            dgvHistory.DataSource = table;
            dgvHistory.Columns["id"].HeaderText = "ID";
            dgvHistory.Columns["group_id"].HeaderText = "组ID";
            dgvHistory.Columns["group_desc"].HeaderText = "描述";
            dgvHistory.Columns["service_type"].HeaderText = "服务类型";
            dgvHistory.Columns["provider_peer_id"].HeaderText = "提供者";
            dgvHistory.Columns["consumer_peer_id"].HeaderText = "消费者";
            dgvHistory.Columns["created_at"].HeaderText = "创建时间";
        }

        private void BtnLoadHistory_Click(object sender, EventArgs e)
        {
            if (dgvHistory.SelectedRows.Count == 0) return;

            int id = Convert.ToInt32(dgvHistory.SelectedRows[0].Cells["id"].Value);
            string sql = "SELECT * FROM configs WHERE id = @id";

            using (var cmd = new SQLiteCommand(sql, dbConnection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        txtGroupID.Text = reader["group_id"].ToString();
                        txtGroupKey.Text = reader["group_key"].ToString();
                        txtGroupDesc.Text = reader["group_desc"].ToString();
                        cmbServiceType.Text = reader["service_type"].ToString();
                        txtServerIP1.Text = reader["server_ip1"].ToString();
                        txtServerIP2.Text = reader["server_ip2"].ToString();
                        numServerPort.Value = Convert.ToInt32(reader["server_port"]);
                        numLocalPort.Value = Convert.ToInt32(reader["local_port"]);
                        numTargetPort.Value = Convert.ToInt32(reader["target_port"]);
                        txtProviderPeerID.Text = reader["provider_peer_id"].ToString();
                        txtConsumerPeerID.Text = reader["consumer_peer_id"].ToString();

                        tabControl.SelectedIndex = 0;
                        MessageBox.Show("配置已加载，可以直接生成或修改后生成", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dbConnection?.Close();
            base.OnFormClosing(e);
        }
    }

    // 扩展方法
    public static class Extensions
    {
        public static T Also<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
