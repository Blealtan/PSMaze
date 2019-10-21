using namespace System.Collections.Generic
# 初始化数据结构
$queue = [Queue[string]]::new()
$queue.Enqueue("Maze:/")
$visited = [HashSet[Tuple[int, int]]]::new()
# 开始搜索循环
while ($queue.Count -ne 0) {
    $path = $queue.Dequeue()
    $gi = Get-Item $path
    $xy = [Tuple[int, int]]::new($gi.X, $gi.Y)
    if ($visited.Contains($xy)) { continue }
    $visited.Add($xy) | Out-Null
    if ($gi.Flag -ne $null) {
        Write-Output $path
        Write-Output $gi.Flag
        break
    }
    Get-ChildItem $path | Foreach-Object { $queue.Enqueue("$path/$($_.Direction)") } | Out-Null
}