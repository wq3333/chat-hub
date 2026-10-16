using Microsoft.VisualStudio.TestTools.UnitTesting;

// 测试使用进程级临时目录与共享的可变状态，显式关闭并行执行以保证结果稳定。
[assembly: DoNotParallelize]
