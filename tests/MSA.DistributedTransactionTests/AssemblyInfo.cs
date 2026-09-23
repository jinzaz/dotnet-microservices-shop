// 这些测试共用同一个测试库 schema，并在每个用例开始时清空表；并行执行会互相踩踏。
[assembly: DoNotParallelize]
