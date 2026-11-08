using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NexusOptimizer.Views;

public static class UiEntranceAnimator
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> AnimatedPages = new();

    public static void AnimateOnce(FrameworkElement page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (AnimatedPages.TryGetValue(page, out _))
            return;

        page.UpdateLayout();
        if (page.ActualWidth <= 0 || page.ActualHeight <= 0)
            return;

        FrameworkElement? root = page switch
        {
            UserControl userControl => userControl.Content as FrameworkElement,
            Window window => window.Content as FrameworkElement,
            _ => page
        };
        if (root is null)
            return;

        root.UpdateLayout();
        List<FrameworkElement> targets = GetTargets(root);
        if (targets.Count == 0)
            return;

        AnimatedPages.Add(page, new object());
        for (int index = 0; index < targets.Count; index++)
            Animate(targets[index], TimeSpan.FromMilliseconds(index * 45));
    }

    private static List<FrameworkElement> GetTargets(FrameworkElement root)
    {
        var candidates = new List<FrameworkElement>();
        if (root is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is ScrollViewer { Content: Panel contentPanel })
                {
                    foreach (UIElement content in contentPanel.Children)
                        AddVisibleTarget(candidates, content);
                }
                else
                {
                    AddVisibleTarget(candidates, child);
                }
            }
        }
        else
        {
            AddVisibleTarget(candidates, root);
        }

        return candidates;
    }

    private static void AddVisibleTarget(List<FrameworkElement> targets, UIElement element)
    {
        if (element is FrameworkElement frameworkElement
            && frameworkElement.Visibility == Visibility.Visible
            && frameworkElement.ActualWidth > 0
            && frameworkElement.ActualHeight > 0)
        {
            targets.Add(frameworkElement);
        }
    }

    private static void Animate(FrameworkElement element, TimeSpan delay)
    {
        double originalOpacity = element.Opacity;
        Transform originalTransform = element.RenderTransform;
        var translation = new TranslateTransform(0, 14);

        if (originalTransform == Transform.Identity)
        {
            element.RenderTransform = translation;
        }
        else
        {
            var group = new TransformGroup();
            group.Children.Add(originalTransform);
            group.Children.Add(translation);
            element.RenderTransform = group;
        }

        element.Opacity = 0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, originalOpacity, TimeSpan.FromMilliseconds(280))
        {
            BeginTime = delay,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = originalOpacity;
        };

        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320))
        {
            BeginTime = delay,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
        slide.Completed += (_, _) =>
        {
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            translation.Y = 0;
        };

        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        translation.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }
}
