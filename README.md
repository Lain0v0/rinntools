# Rinn Tools

VRChat 改模用的 Unity 编辑器工具。需要 VRChat Avatars SDK 3.10.5 或更高版本。

## 安装

在 VCC 的社区仓库里添加：

https://lain0v0.github.io/rinntools/index.json

然后在项目中安装 rinntools。

## 服装匹配

菜单：`Tools / Rinn's Tools / Clothing Adjustment`

把衣服骨骼按自己设定规则对齐到模型骨骼。先指定源模型，再指定目标衣服。

规则可以保存成预设，自带的标准预设是我自己修改的Avatar使用的，可以参考我的预设进行修改。

支持的调整：

- 增量位移、旋转、缩放：在当前 localPosition 基础上【加上】填写的值。
- 绝对值位移、旋转、缩放：把 localPosition 【直接设为】填写的值，无视原值。
- 复制世界位置、旋转、缩放：把目标的【世界坐标】对齐到源骨骼（自动换算回本地空间）。

轴掩码可以只改选中的轴。

应用前可以预览，关闭窗口会还原预览。

支持中、英显示。

## 文件夹预览

菜单：`Tools / Rinn's Tools / Folder Preview Settings`

Project 窗口里，如果文件夹内有`folder.jpg`，就用这张图代替文件夹图标。默认开启，可在设置里关闭，也可以刷新缓存。界面支持中文、日文和英文。
