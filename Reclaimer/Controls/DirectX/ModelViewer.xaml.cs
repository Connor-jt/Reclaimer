﻿using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;
using Reclaimer.Blam.Utilities;
using Reclaimer.Geometry;
using Reclaimer.Models;
using Reclaimer.Plugins;
using Reclaimer.Utilities;
using Studio.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Reclaimer.Controls.DirectX
{
    /// <summary>
    /// Interaction logic for ModelViewer.xaml
    /// </summary>
    public partial class ModelViewer : IDisposable
    {
        private delegate void ExportBitmaps(IRenderGeometry geometry);
        private delegate void ExportSelectedBitmaps(IRenderGeometry geometry, IEnumerable<int> shaderIndexes);

        private static readonly string[] AllLods = new[] { "Highest", "High", "Medium", "Low", "Lowest", "Potato" };

        #region Dependency Properties
        private static readonly DependencyPropertyKey AvailableLodsPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(AvailableLods), typeof(IEnumerable<string>), typeof(ModelViewer), new PropertyMetadata());

        public static readonly DependencyProperty AvailableLodsProperty = AvailableLodsPropertyKey.DependencyProperty;

        public static readonly DependencyProperty SelectedLodProperty =
            DependencyProperty.Register(nameof(SelectedLod), typeof(int), typeof(ModelViewer), new PropertyMetadata(0, SelectedLodChanged));

        public IEnumerable<string> AvailableLods
        {
            get => (IEnumerable<string>)GetValue(AvailableLodsProperty);
            private set => SetValue(AvailableLodsPropertyKey, value);
        }

        public int SelectedLod
        {
            get => (int)GetValue(SelectedLodProperty);
            set => SetValue(SelectedLodProperty, value);
        }

        //TODO: LODs
        public static void SelectedLodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            //var control = (ModelViewer)d;
            //control.SetLod((int)e.NewValue);
        }
        #endregion

        private readonly GroupModel3D modelGroup = new GroupModel3D { IsHitTestVisible = false };

        private ICachedContentProvider<Scene> sceneProvider;
        private TextureLoader textureLoader;

        public TabModel TabModel { get; }
        public ObservableCollection<TreeItemModel> TreeViewItems { get; }

        public Action<string> LogOutput { get; set; }
        public Action<string, Exception> LogError { get; set; }
        public Action<string> SetStatus { get; set; }
        public Action ClearStatus { get; set; }

        public ModelViewer()
        {
            TabModel = new TabModel(this, TabItemType.Document);
            TreeViewItems = new ObservableCollection<TreeItemModel>();
            InitializeComponent();

            renderer.AddChild(modelGroup);
        }

        public void LoadGeometry(IContentProvider<Scene> provider)
        {
            sceneProvider = provider.AsCached();
            TabModel.ToolTip = provider.Name;
            TabModel.Header = Utils.GetFileName(provider.Name);

            AvailableLods = AllLods.Take(1); // model.LodCount);

            TreeViewItems.Clear();
            ClearChildren();

            var scene = sceneProvider.Content;
            textureLoader = new TextureLoader(scene);

            renderer.SetCoordinateSystem(scene.CoordinateSystem);

            if (scene.ChildGroups.Count == 0 && scene.ChildObjects.Count == 1 && scene.ChildObjects[0] is Model m)
            {
                //load in single-model mode - displays permutation tree instead of object tree
                LoadGeometry(m);
                return;
            }

            var isFirst = true;
            var sceneBounds = default(SharpDX.BoundingBox);

            AppendSceneGroups(scene.ChildGroups, TreeViewItems);
            renderer.SetDefaultBounds(sceneBounds);
            return;

            void AppendSceneGroups(List<SceneGroup> groups, ObservableCollection<TreeItemModel> destination)
            {
                foreach (var group in groups.OrderBy(g => g.Name))
                {
                    var groupNode = new TreeItemModel { Header = group.Name, IsChecked = true };

                    AppendSceneGroups(group.ChildGroups, groupNode.Items);

                    foreach (var obj in group.ChildObjects.OrderBy(o => o.Name))
                    {
                        var placement = obj as ObjectPlacement;

                        if ((placement?.Object ?? obj) is not Model model)
                            continue;

                        var objNode = new TreeItemModel { Header = model.Name, IsChecked = true };
                        var meshLoader = new MeshLoader(model, textureLoader);
                        var objGroup = new GroupModel3D();

                        if (placement != null && !placement.Transform.IsIdentity)
                        {
                            objGroup.Transform = placement.Transform.ToMediaTransform();
                            objGroup.Transform.Freeze();
                        }

                        foreach (var region in model.Regions)
                        {
                            foreach (var perm in region.Permutations)
                            {
                                if (perm.MeshRange.Count <= 0)
                                    continue;

                                var tag = meshLoader.GetMesh(perm);
                                if (tag == null)
                                    continue;

                                if (!objGroup.Children.Contains(tag.Mesh))
                                    objGroup.Children.Add(tag.Mesh);
                            }
                        }

                        if (!objGroup.Children.Any())
                            continue;

                        modelGroup.Children.Add(objGroup);

                        if (model.Flags.HasFlag(SceneFlags.PrimaryFocus))
                        {
                            var objBounds = objGroup.GetTotalBounds();
                            if (!isFirst)
                                SharpDX.BoundingBox.Merge(ref sceneBounds, ref objBounds, out sceneBounds);
                            else
                            {
                                isFirst = false;
                                sceneBounds = objBounds;
                            }
                        }

                        var objTag = new MeshTag(null, objGroup);
                        objNode.Tag = objTag;
                        groupNode.Items.Add(objNode);
                    }

                    if (groupNode.HasItems)
                        destination.Add(groupNode);
                }
            }
        }

        public void LoadGeometry(Model model)
        {
            var meshLoader = new MeshLoader(model, textureLoader);

            foreach (var region in model.Regions)
            {
                var regNode = new TreeItemModel { Header = region.Name, IsChecked = true };

                foreach (var perm in region.Permutations)
                {
                    if (perm.MeshRange.Count <= 0)
                        continue;

                    var tag = meshLoader.GetMesh(perm);
                    if (tag == null)
                        continue;

                    var permNode = new TreeItemModel { Header = perm.Name, IsChecked = true };
                    regNode.Items.Add(permNode);

                    permNode.Tag = tag;
                    if (!modelGroup.Children.Contains(tag.Mesh))
                        modelGroup.Children.Add(tag.Mesh);
                }

                if (regNode.HasItems)
                    TreeViewItems.Add(regNode);
            }
        }

        private IEnumerable<ModelPermutation> GetSelectedPermutations()
        {
            return TreeViewItems.Where(i => i.IsChecked != false)
                .SelectMany(i => i.Items.Where(ii => ii.IsChecked == true))
                .Select(i => (i.Tag as MeshTag).Permutation);
        }

        #region Treeview Events
        private void TreeViewItem_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement).DataContext as TreeItemModel;
            if (item != tv.SelectedItem)
                return; //because this event bubbles to the parent node

            if (item.Tag is not MeshTag t)
                return;

            if (t.Instance != null)
            {
                var bounds = Enumerable.Repeat(t.Mesh, 1).GetTotalBounds(true);
                var newMin = SharpDX.Vector3.TransformCoordinate(bounds.Minimum, t.Instance.Transform);
                var newMax = SharpDX.Vector3.TransformCoordinate(bounds.Maximum, t.Instance.Transform);
                var box = new SharpDX.BoundingBox(newMin, newMax);
                renderer.ZoomToBounds(box);
            }
            else
                renderer.LocateObject(t.Mesh);
        }

        private bool isWorking = false;

        private void TreeViewItem_Checked(object sender, RoutedEventArgs e)
        {
            if (isWorking)
                return;

            isWorking = true;
            SetState((e.OriginalSource as FrameworkElement).DataContext as TreeItemModel, true);
            isWorking = false;
        }

        private void SetState(TreeItemModel item, bool updateRender)
        {
            foreach (var child in EnumerateDescendents(item).Prepend(item))
            {
                child.IsChecked = item.IsChecked;
                if (updateRender && child.Tag is MeshTag tag)
                    tag.SetVisible(child.IsChecked.GetValueOrDefault());
            }

            RefreshState(item.Parent);

            void RefreshState(TreeItemModel item)
            {
                if (item == null || !item.HasItems)
                    return;

                var prev = item.IsChecked;
                item.IsChecked = item.Items
                    .Where(i => i.IsVisible)
                    .Select(i => i.IsChecked)
                    .Aggregate(item.Items[0].IsChecked, (a, b) => a == null || a != b ? null : a);

                //if this node changed state then the parent needs to refresh state too
                if (item.IsChecked != prev)
                    RefreshState(item.Parent);
            }

            IEnumerable<TreeItemModel> EnumerateDescendents(TreeItemModel item)
            {
                var visible = item.Items.Where(i => i.IsVisible);
                return visible.Concat(visible.SelectMany(EnumerateDescendents));
            }
        }
        #endregion

        #region Toolbar Events
        private void btnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in TreeViewItems)
                item.IsExpanded = false;
        }

        private void btnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in TreeViewItems)
                item.IsExpanded = true;
        }

        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in TreeViewItems)
                item.IsChecked = true;
        }

        private void btnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in TreeViewItems)
                item.IsChecked = false;
        }

        #region Export Functions
        private bool PromptFileSave(out string fileName, out string formatId)
        {
            var exportFormats = ModelViewerPlugin.GetExportFormats()
                .Select(f => new
                {
                    FormatId = f,
                    Extension = ModelViewerPlugin.GetFormatExtension(f),
                    Description = ModelViewerPlugin.GetFormatDescription(f)
                }).ToList();

            var filter = string.Join("|", exportFormats.Select(f => $"{f.Description}|*.{f.Extension}"));

            var sfd = new SaveFileDialog
            {
                OverwritePrompt = true,
                FileName = Utils.GetFileName(string.IsNullOrWhiteSpace(sceneProvider.Content.Name) ? sceneProvider.Name : sceneProvider.Content.Name),
                Filter = filter,
                FilterIndex = 1 + exportFormats.TakeWhile(f => f.FormatId != ModelViewerPlugin.Settings.DefaultSaveFormat).Count(),
                AddExtension = true
            };

            if (sfd.ShowDialog() != true)
            {
                fileName = formatId = null;
                return false;
            }

            fileName = sfd.FileName;
            formatId = exportFormats[sfd.FilterIndex - 1].FormatId;
            ModelViewerPlugin.Settings.DefaultSaveFormat = formatId;
            return true;
        }

        private void btnExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptFileSave(out var fileName, out var formatId))
                return;

            ModelViewerPlugin.WriteModelFile(sceneProvider, fileName, formatId);
        }

        private void btnExportSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptFileSave(out var fileName, out var formatId))
                return;

            //TODO: export only selected permutations
            //var masked = new MaskedGeometryModel(model, GetSelectedPermutations());
            //ModelViewerPlugin.WriteModelFile(masked, fileName, formatId);
        }

        //TODO: bitmap exports
        private void btnExportBitmaps_Click(object sender, RoutedEventArgs e)
        {
            var export = Substrate.GetSharedFunction<ExportBitmaps>(Constants.SharedFuncExportBitmaps);
            //export.Invoke(geometry);
        }

        private void btnExportSelectedBitmaps_Click(object sender, RoutedEventArgs e)
        {
            var export = Substrate.GetSharedFunction<ExportSelectedBitmaps>(Constants.SharedFuncExportSelectedBitmaps);
            //var matIndices = GetSelectedPermutations()
            //    .SelectMany(p => Enumerable.Range(p.MeshRange.Index, p.MeshRange.Count))
            //    .Select(i => model.Meshes.ElementAtOrDefault(i))
            //    .Where(m => m != null)
            //    .SelectMany(m => m.Submeshes.Select(s => (int)s.MaterialIndex))
            //    .Distinct();
            //
            //export.Invoke(geometry, matIndices);
        }
        #endregion
        #endregion

        #region Control Events
        private void txtSearch_SearchChanged(object sender, RoutedEventArgs e)
        {
            foreach (var parent in TreeViewItems)
            {
                foreach (var child in parent.Items)
                {
                    child.Visibility = string.IsNullOrEmpty(txtSearch.Text) || child.Header.ToUpperInvariant().Contains(txtSearch.Text.ToUpperInvariant())
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                parent.Visibility = parent.Items.Any(i => i.IsVisible)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            isWorking = true;
            foreach (var item in TreeViewItems)
                SetState(item.Items.First(), false);
            isWorking = false;
        }

        private void btnToggleDetails_Click(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleButton;
            if (toggle.IsChecked == true)
            {
                toggle.Tag = SplitPanel.GetDesiredSize(splitPanel.Children[0]);
                SplitPanel.SetDesiredSize(splitPanel.Children[0], new GridLength(0));
                splitPanel.SplitterThickness = 0;
            }
            else
            {
                SplitPanel.SetDesiredSize(splitPanel.Children[0], (GridLength)toggle.Tag);
                splitPanel.SplitterThickness = 3;
            }
        }

        private void PosLabel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Label label)
                return;

            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(1)
            };

            Clipboard.SetText(label.Content?.ToString());
            label.BeginAnimation(OpacityProperty, anim);
        }

        private void ClearChildren()
        {
            foreach (var element in modelGroup.Children.ToList())
            {
                modelGroup.Children.Remove(element);
                element.Dispose();
            }
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            TreeViewItems.Clear();
            ClearChildren();
            modelGroup.Dispose();
            renderer.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        #endregion

        private sealed class MeshTag
        {
            public ModelPermutation Permutation { get; }
            public GroupElement3D Mesh { get; }
            public MeshLoader.InstancedPermutation Instance { get; }

            public MeshTag(ModelPermutation permutation, GroupElement3D mesh)
            {
                Permutation = permutation;
                Mesh = mesh;
            }

            public MeshTag(ModelPermutation permutation, GroupElement3D mesh, MeshLoader.InstancedPermutation instance)
                : this(permutation, mesh)
            {
                Instance = instance;
            }

            public MeshTag(ModelPermutation permutation, InstancingMeshGeometryModel3D mesh, Guid instanceId)
            {
                Permutation = permutation;
            }

            public void SetVisible(bool visible)
            {
                if (Instance != null)
                    Instance.Toggle(visible);
                else
                    Mesh.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
