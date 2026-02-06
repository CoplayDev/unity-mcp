import inspect

# pyright: reportMissingImports=false


def test_manage_scene_signature_includes_paging_params():
    import services.tools.manage_scene as mod

    sig = inspect.signature(mod.manage_scene)
    names = list(sig.parameters.keys())

    # get_hierarchy paging/safety params
    assert "parent" in names
    assert "page_size" in names
    assert "cursor" in names
    assert "max_nodes" in names
    assert "max_depth" in names
    assert "max_children_per_node" in names
    assert "include_transform" in names


def test_manage_gameobject_signature_excludes_vestigial_params():
    """Paging/find/component params were removed — they belong to separate tools."""
    import services.tools.manage_gameobject as mod

    sig = inspect.signature(mod.manage_gameobject)
    names = list(sig.parameters.keys())

    # These params now live on find_gameobjects / manage_components / gameobject_components resource
    assert "page_size" not in names
    assert "cursor" not in names
    assert "max_components" not in names
    assert "include_properties" not in names
    assert "search_term" not in names
    assert "find_all" not in names
    assert "search_in_children" not in names
    assert "search_inactive" not in names
    assert "component_name" not in names
    assert "includeNonPublicSerialized" not in names


