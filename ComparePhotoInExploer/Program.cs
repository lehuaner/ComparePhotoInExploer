using System.IO.Pipes;

namespace ComparePhotoInExploer;

static class Program
{
    // 单实例互斥体名称 - 使用 Global\ 前缀确保跨会话可见
    private const string MutexName = @"Global\ComparePhotoInExploer_SingleInstance";
    // Named Pipe 名称，用于进程间通信
    private const string PipeName = "ComparePhotoInExploer_IPC";

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (s, e) =>
        {
            MessageBox.Show($"UI线程异常:\n{e.Exception}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            MessageBox.Show($"未处理异常:\n{e.ExceptionObject}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        // 尝试获取互斥体，判断是否已有实例运行
        bool createdNew;
        using var mutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // 已有实例运行，将参数通过 Named Pipe 发送给已有实例
            SendArgsToExistingInstance(args);
            return;
        }

        // 第一个实例：在 Application.Run 之前就启动 Pipe Server
        // 这样后续实例可以立即连接，避免竞态条件
        var cts = new CancellationTokenSource();
        var pipeReady = new ManualResetEvent(false);
        var listenerThread = new Thread(() => ListenForArgs(cts.Token, pipeReady))
        {
            IsBackground = true
        };
        listenerThread.Start();

        // 等待 Pipe Server 准备就绪（最多3秒）
        pipeReady.WaitOne(3000);

        // 运行主窗体
        try
        {
            Application.Run(new Form1(args));
        }
        finally
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// 向已有实例发送命令行参数（带重试）
    /// </summary>
    private static void SendArgsToExistingInstance(string[] args)
    {
        if (args == null || args.Length == 0) return;

        // 重试连接，最多20次，每次间隔200ms（总共4秒）
        // 第一个实例需要时间启动 Pipe Server
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000);

                using var writer = new StreamWriter(client) { AutoFlush = true };
                // 每行一个参数
                foreach (var arg in args)
                {
                    writer.WriteLine(arg);
                }
                // 发送空行表示结束
                writer.WriteLine();
                return; // 发送成功
            }
            catch
            {
                // 连接失败，等待后重试
                Thread.Sleep(200);
            }
        }
        // 所有重试均失败，静默退出
    }

    /// <summary>
    /// 监听后续实例发来的参数，并在主线程更新 UI
    /// </summary>
    private static void ListenForArgs(CancellationToken token, ManualResetEvent pipeReady)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                // 通知 Pipe Server 已准备好
                pipeReady.Set();

                // 异步等待连接
                var waitTask = server.WaitForConnectionAsync(token);
                try
                {
                    waitTask.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (AggregateException)
                {
                    return;
                }

                if (!server.IsConnected)
                    continue;

                using var reader = new StreamReader(server);
                var newArgs = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line))
                        break; // 空行表示结束
                    newArgs.Add(line);
                }

                if (newArgs.Count > 0)
                {
                    // 在 UI 线程添加图片
                    var capturedArgs = newArgs.ToArray();
                    // 等待主窗体就绪（最多5秒），避免首个实例还在初始化时参数丢失
                    Form1ReadyEvent.WaitOne(5000);
                    Form1?.BeginInvoke(new Action(() =>
                    {
                        Form1?.AddImagesFromExternal(capturedArgs);
                    }));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 忽略管道异常，继续监听
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// 主窗体引用，用于跨线程调用
    /// </summary>
    public static Form1? Form1 { get; set; }

    /// <summary>
    /// 主窗体就绪事件，用于 IPC 线程等待 Form1 初始化完成
    /// </summary>
    public static ManualResetEvent Form1ReadyEvent { get; } = new(false);
}
