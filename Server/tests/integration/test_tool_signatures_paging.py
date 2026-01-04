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


def test_manage_gameobject_signature_includes_paging_params():
    import services.tools.manage_gameobject as mod

    sig = inspect.signature(mod.manage_gameobject)
    names = list(sig.parameters.keys())

    assert "page_size" in names
    assert "cursor" in names
    assert "max_components" in names
    assert "include_properties" in names


