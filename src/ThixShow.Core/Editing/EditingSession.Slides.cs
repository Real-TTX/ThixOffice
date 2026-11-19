using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

public sealed partial class EditingSession
{
    /// <summary>
    /// Adds a slide of <paramref name="layoutId"/> (null: the current slide's layout; on a title slide "Titel und Inhalt") after the
    /// current slide, in its section, and shows it.
    /// </summary>
    public uint AddSlide(uint? layoutId = null)
    {
        var id = Presentation.NextSlideId;
        var current = CurrentSlide;
        Apply("Neue Folie", presentation =>
        {
            var layout = layoutId is { } chosen ? presentation.FindLayout(chosen) : null;
            if (layout is null && current is not null)
            {
                var currentLayout = presentation.LayoutOf(current);
                layout = currentLayout.Type == "title"
                    ? presentation.MasterOf(current).Layouts.FirstOrDefault(item => item.Type == "obj") ?? currentLayout
                    : currentLayout;
            }

            layout ??= presentation.Masters[0].Layouts.FirstOrDefault(item => item.Type == "obj") ?? presentation.Masters[0].Layouts[0];
            var slide = MasterFactory.CreateSlide(layout, id);
            var index = current is null ? presentation.Slides.Count : presentation.IndexOf(current.Id) + 1;
            return presentation with
            {
                Slides = presentation.Slides.Insert(index, slide),
                Sections = AddToSection(presentation.Sections, current?.Id, id),
            };
        }, _ => ShowSelection.OnSlide(id));
        return id;
    }

    private static ValueList<SlideSection> AddToSection(ValueList<SlideSection> sections, uint? after, uint id)
    {
        if (sections.IsEmpty)
        {
            return sections;
        }

        var index = after is { } previous ? sections.FindIndex(section => section.SlideIds.Contains(previous)) : sections.Count - 1;
        index = index < 0 ? sections.Count - 1 : index;
        var section = sections[index];
        var position = after is { } previousId ? section.SlideIds.IndexOf(previousId) + 1 : section.SlideIds.Count;
        return sections.SetItem(index, section with { SlideIds = section.SlideIds.Insert(Math.Max(0, position), id) });
    }

    /// <summary>Duplicates the given slides (each right after itself) and selects the copies.</summary>
    public IReadOnlyList<uint> DuplicateSlides(IReadOnlyList<uint> slideIds)
    {
        var copies = new List<uint>();
        Apply("Folie duplizieren", presentation =>
        {
            var next = presentation.NextSlideId;
            var result = presentation;
            foreach (var id in slideIds.OrderByDescending(presentation.IndexOf))
            {
                var index = result.IndexOf(id);
                if (index < 0)
                {
                    continue;
                }

                var copy = Copy(result.Slides[index], next);
                copies.Add(next);
                result = result with
                {
                    Slides = result.Slides.Insert(index + 1, copy),
                    Sections = AddToSection(result.Sections, id, next),
                };
                next++;
            }

            return result;
        }, _ => new ShowSelection(copies.LastOrDefault(), copies));
        return copies;
    }

    /// <summary>A copy of a slide under a new id: a slide of its own in the file (no part), notes copied likewise.</summary>
    internal static Slide Copy(Slide slide, uint id) => slide with
    {
        Id = id,
        PartUri = null,
        Notes = slide.Notes is null ? null : slide.Notes with { PartUri = null },
    };

    /// <summary>Deletes slides (never the last one); the slide after the first deleted becomes current.</summary>
    public void DeleteSlides(IReadOnlyList<uint> slideIds)
    {
        var ids = slideIds.ToHashSet();
        if (ids.Count == 0 || Presentation.Slides.All(slide => ids.Contains(slide.Id)))
        {
            return;
        }

        var firstIndex = Presentation.Slides.FindIndex(slide => ids.Contains(slide.Id));
        Apply(ids.Count == 1 ? "Folie löschen" : "Folien löschen", presentation =>
        {
            var slides = presentation.Slides.RemoveAll(slide => ids.Contains(slide.Id));
            slides = new ValueList<Slide>(slides.Select(slide => RemoveLinksTo(slide, ids)));
            return presentation with
            {
                Slides = slides,
                Sections = new ValueList<SlideSection>(presentation.Sections.Select(section => section with { SlideIds = section.SlideIds.RemoveAll(ids.Contains) })),
            };
        }, presentation => ShowSelection.OnSlide(presentation.Slides[Math.Min(firstIndex, presentation.Slides.Count - 1)].Id));
    }

    /// <summary>A slide whose links to deleted slides are gone (they would point nowhere).</summary>
    private static Slide RemoveLinksTo(Slide slide, HashSet<uint> deleted)
    {
        bool Dead(Hyperlink? link) => link is { Kind: HyperlinkKind.Slide, SlideId: { } target } && deleted.Contains(target);
        SlideElement Clean(SlideElement shape)
        {
            var result = Dead(shape.Click) ? shape with { Click = null } : shape;
            if (result is AutoShape { Text: { } text } auto && text.Paragraphs.Any(paragraph => paragraph.Runs.Any(run => Dead(run.Properties.Click))))
            {
                result = auto with
                {
                    Text = text with
                    {
                        Paragraphs = text.Paragraphs.Select(paragraph => paragraph with
                        {
                            Runs = paragraph.Runs.Select(run => Dead(run.Properties.Click) && run is TextSpan span ? span with { RunProperties = span.RunProperties with { Click = null } } : run).ToList(),
                        }).ToList(),
                    },
                };
            }

            return result is GroupShape group ? group with { Children = group.Children.Select(Clean).ToList() } : result;
        }

        var shapes = slide.Shapes.Select(Clean).ToList();
        return shapes.Zip(slide.Shapes).All(pair => ReferenceEquals(pair.First, pair.Second)) ? slide : slide with { Shapes = shapes };
    }

    /// <summary>Moves slides (in their order) so that they start at <paramref name="index"/> of the remaining slides.</summary>
    public void MoveSlides(IReadOnlyList<uint> slideIds, int index)
    {
        Apply("Folien verschieben", presentation =>
        {
            var moving = presentation.Slides.Where(slide => slideIds.Contains(slide.Id)).ToList();
            if (moving.Count == 0)
            {
                return presentation;
            }

            var rest = presentation.Slides.Where(slide => !slideIds.Contains(slide.Id)).ToList();
            var target = Math.Clamp(index, 0, rest.Count);
            rest.InsertRange(target, moving);
            if (rest.Select(slide => slide.Id).SequenceEqual(presentation.Slides.Select(slide => slide.Id)))
            {
                return presentation;
            }

            var sections = presentation.Sections;
            if (!sections.IsEmpty)
            {
                // A moved slide joins the section of the slide before it (or the first section).
                var previous = target > 0 ? rest[target - 1].Id : (uint?)null;
                var cleaned = sections.Select(section => section with { SlideIds = section.SlideIds.RemoveAll(slideIds.Contains) }).ToList();
                var sectionIndex = previous is { } before ? Math.Max(0, cleaned.FindIndex(section => section.SlideIds.Contains(before))) : 0;
                var position = previous is { } after ? cleaned[sectionIndex].SlideIds.IndexOf(after) + 1 : 0;
                cleaned[sectionIndex] = cleaned[sectionIndex] with { SlideIds = cleaned[sectionIndex].SlideIds.InsertRange(position, moving.Select(slide => slide.Id)) };
                sections = cleaned;
            }

            return presentation with { Slides = rest, Sections = sections };
        });
    }

    public void SetSlidesHidden(IReadOnlyList<uint> slideIds, bool hidden) =>
        Apply(hidden ? "Folie ausblenden" : "Folie einblenden", presentation =>
            slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide => slide.Hidden == hidden ? slide : slide with { Hidden = hidden })));

    /// <summary>
    /// Gives slides another layout: placeholders are matched by type (content by index/order) and keep their content and own
    /// formatting; placeholders the new layout lacks become ordinary shapes at their current position; empty new placeholders appear.
    /// </summary>
    public void ChangeLayout(IReadOnlyList<uint> slideIds, uint layoutId) =>
        Apply("Layout", presentation =>
        {
            var layout = presentation.FindLayout(layoutId);
            if (layout is null)
            {
                return presentation;
            }

            return slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide => slide.LayoutId == layoutId ? slide : Relayout(current, slide, layout)));
        });

    private static Slide Relayout(Presentation presentation, Slide slide, SlideLayout layout)
    {
        var context = Formatting.SlideContext.For(presentation, slide);
        var targets = layout.Shapes.Where(shape => shape.Placeholder is { IsFooterKind: false }).ToList();
        var used = new HashSet<SlideElement>();
        var shapes = new List<SlideElement>();
        foreach (var shape in slide.Shapes)
        {
            if (shape.Placeholder is not { } placeholder || placeholder.IsFooterKind)
            {
                shapes.Add(shape);
                continue;
            }

            var match = targets.FirstOrDefault(target => !used.Contains(target) && target.Placeholder!.Type == placeholder.Type)
                        ?? targets.FirstOrDefault(target => !used.Contains(target) && target.Placeholder!.MasterType == placeholder.MasterType && !target.Placeholder.IsTitle == !placeholder.IsTitle);
            if (match is not null)
            {
                used.Add(match);
                shapes.Add(shape with { Placeholder = match.Placeholder! with { HasCustomPrompt = false } });
                continue;
            }

            if (shape is AutoShape { Text: { IsEmpty: true } } || shape is AutoShape { Text: null })
            {
                continue; // an empty placeholder without a place in the new layout goes away
            }

            // Content without a place in the new layout stays as an ordinary shape where it is.
            var resolved = Formatting.ShapeResolver.Resolve(context, shape);
            shapes.Add(shape with { Placeholder = null, Transform = resolved.Transform });
        }

        var nextId = Math.Max(slide.MaxShapeId, 1) + 1;
        foreach (var target in targets.Where(target => !used.Contains(target)))
        {
            shapes.Add(new AutoShape
            {
                Id = nextId,
                Name = MasterFactory.PlaceholderName(target.Placeholder!, nextId),
                Placeholder = target.Placeholder! with { HasCustomPrompt = false },
                Text = TextBody.Empty,
            });
            nextId++;
        }

        return slide with { LayoutId = layout.Id, Shapes = shapes };
    }

    /// <summary>
    /// "Zurücksetzen": placeholders lose their own position, size and formatting (they follow the layout again), missing placeholders
    /// of the layout come back.
    /// </summary>
    public void ResetSlides(IReadOnlyList<uint> slideIds) =>
        Apply("Folie zurücksetzen", presentation => slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide =>
        {
            var shapes = slide.Shapes.Select(shape => shape switch
            {
                AutoShape { Placeholder: not null } auto => auto with
                {
                    Transform = null,
                    Geometry = null,
                    Fill = null,
                    Outline = null,
                    Effects = null,
                    Text = auto.Text is null ? null : auto.Text with { Body = BodyProperties.Empty, ListStyle = ListStyle.Empty },
                },
                _ => shape,
            }).ToList();
            var reset = slide with { Shapes = shapes };
            var layout = current.LayoutOf(slide);
            return Relayout(current, reset with { LayoutId = 0 }, layout) with { LayoutId = layout.Id };
        })));

    // ------------------------------------------------------------ sections

    /// <summary>Adds a section starting at <paramref name="firstSlideId"/> (the slides up to the next section join it).</summary>
    public string AddSection(string name, uint firstSlideId)
    {
        var id = Guid.NewGuid().ToString("B").ToUpperInvariant();
        Apply("Abschnitt hinzufügen", presentation =>
        {
            var index = presentation.IndexOf(firstSlideId);
            if (index < 0)
            {
                return presentation;
            }

            var sections = presentation.Sections.ToList();
            if (sections.Count == 0)
            {
                var before = presentation.Slides.Take(index).Select(slide => slide.Id).ToList();
                if (before.Count > 0)
                {
                    sections.Add(new SlideSection("Standardabschnitt", Guid.NewGuid().ToString("B").ToUpperInvariant(), before));
                }

                sections.Add(new SlideSection(name, id, presentation.Slides.Skip(index).Select(slide => slide.Id).ToList()));
                return presentation with { Sections = sections };
            }

            var owner = sections.FindIndex(section => section.SlideIds.Contains(firstSlideId));
            if (owner < 0)
            {
                return presentation;
            }

            var split = sections[owner].SlideIds.IndexOf(firstSlideId);
            var moved = sections[owner].SlideIds.Skip(split).ToList();
            sections[owner] = sections[owner] with { SlideIds = sections[owner].SlideIds.Take(split).ToList() };
            sections.Insert(owner + 1, new SlideSection(name, id, moved));
            return presentation with { Sections = sections.Where(section => section.SlideIds.Count > 0 || section.Id == id).ToList() };
        });
        return id;
    }

    public void RenameSection(string sectionId, string name) =>
        Apply("Abschnitt umbenennen", presentation =>
        {
            var index = presentation.Sections.FindIndex(section => section.Id == sectionId);
            return index < 0 || presentation.Sections[index].Name == name ? presentation : presentation with { Sections = presentation.Sections.SetItem(index, presentation.Sections[index] with { Name = name }) };
        });

    /// <summary>Removes a section; its slides join the section before (or are deleted with it when <paramref name="withSlides"/>).</summary>
    public void RemoveSection(string sectionId, bool withSlides = false)
    {
        var section = Presentation.Sections.FirstOrDefault(item => item.Id == sectionId);
        if (section is null)
        {
            return;
        }

        if (withSlides && section.SlideIds.Count < Presentation.SlideCount)
        {
            Group("Abschnitt und Folien entfernen", () =>
            {
                DeleteSlides(section.SlideIds);
                RemoveSection(sectionId);
            });
            return;
        }

        Apply("Abschnitt entfernen", presentation =>
        {
            var sections = presentation.Sections.ToList();
            var index = sections.FindIndex(item => item.Id == sectionId);
            if (index < 0)
            {
                return presentation;
            }

            if (index > 0)
            {
                sections[index - 1] = sections[index - 1] with { SlideIds = sections[index - 1].SlideIds.AddRange(sections[index].SlideIds) };
            }
            else if (sections.Count > 1)
            {
                sections[1] = sections[1] with { SlideIds = sections[0].SlideIds.AddRange(sections[1].SlideIds) };
            }

            sections.RemoveAt(index);
            return presentation with { Sections = sections };
        });
    }

    /// <summary>Moves a section (with its slides) to position <paramref name="index"/> among the sections.</summary>
    public void MoveSection(string sectionId, int index) =>
        Apply("Abschnitt verschieben", presentation =>
        {
            var sections = presentation.Sections.ToList();
            var from = sections.FindIndex(section => section.Id == sectionId);
            if (from < 0)
            {
                return presentation;
            }

            var section = sections[from];
            sections.RemoveAt(from);
            sections.Insert(Math.Clamp(index, 0, sections.Count), section);
            var order = sections.SelectMany(item => item.SlideIds).ToList();
            var slides = order.Select(id => presentation.FindSlide(id)).OfType<Slide>().ToList();
            slides.AddRange(presentation.Slides.Where(slide => !order.Contains(slide.Id)));
            return presentation with { Sections = sections, Slides = slides };
        });

    /// <summary>Moves slides into a section (at its end).</summary>
    public void MoveSlidesToSection(IReadOnlyList<uint> slideIds, string sectionId)
    {
        var section = Presentation.Sections.FirstOrDefault(item => item.Id == sectionId);
        if (section is null)
        {
            return;
        }

        var remaining = section.SlideIds.Where(id => !slideIds.Contains(id)).ToList();
        var anchor = remaining.Count > 0 ? remaining[^1] : (uint?)null;
        Group("In Abschnitt verschieben", () =>
        {
            var index = anchor is { } last ? Presentation.Slides.Where(slide => !slideIds.Contains(slide.Id)).ToList().FindIndex(slide => slide.Id == last) + 1
                : Presentation.Sections.TakeWhile(item => item.Id != sectionId).Sum(item => item.SlideIds.Count(id => !slideIds.Contains(id)));
            MoveSlides(slideIds, index);
            Apply("In Abschnitt verschieben", presentation => presentation with
            {
                Sections = presentation.Sections.Select(item => item.Id == sectionId
                    ? item with { SlideIds = item.SlideIds.RemoveAll(slideIds.Contains).AddRange(slideIds.Where(id => presentation.FindSlide(id) is not null)) }
                    : item with { SlideIds = item.SlideIds.RemoveAll(slideIds.Contains) }).ToList(),
            });
        });
    }
}
