# Configuration file for the Sphinx documentation builder.
#
# This file only contains a selection of the most common options. For a full
# list see the documentation:
# https://www.sphinx-doc.org/en/master/usage/configuration.html

# -- Path setup --------------------------------------------------------------

# If extensions (or modules to document with autodoc) are in another directory,
# add these directories to sys.path here. If the directory is relative to the
# documentation root, use os.path.abspath to make it absolute, like shown here.
#
import os

# Add an envvar so that the addon knows it's in a sphinx build and can skip some work.
# A custom one is used here instead of RTD's for supporting local builds.
os.environ['SPHINX_BUILD'] = 'true'

import sys
sys.path.insert(0, os.path.abspath('_ext'))
sys.path.insert(0, os.path.abspath('../Blender~'))

# -- Project information -----------------------------------------------------

project = 'Coherence'
copyright = '2021, Chase McManning'
author = 'Chase McManning'

# The full version, including alpha/beta/rc tags
release = '1.0.0a'
version = release


# -- General configuration ---------------------------------------------------

# Add any Sphinx extension module names here, as strings. They can be
# extensions coming with Sphinx (named 'sphinx.ext.*') or your custom
# ones.
extensions = [
    'sphinx.ext.napoleon',
    'sphinx.ext.intersphinx',
    'sphinxsharp.sphinxsharp',
]

# Add any paths that contain templates here, relative to this directory.
templates_path = ['_templates']

# List of patterns, relative to source directory, that match files and
# directories to ignore when looking for source files.
# This pattern also affects html_static_path and html_extra_path.
exclude_patterns = []


# -- Options for HTML output -------------------------------------------------

# The theme to use for HTML and HTML Help pages.  See the documentation for
# a list of builtin themes.
#
import sphinx_rtd_theme
html_theme = 'sphinx_rtd_theme'

# Add any paths that contain custom static files (such as style sheets) here,
# relative to this directory. They are copied after the builtin static files,
# so a file named 'default.css' will overwrite the builtin 'default.css'.
html_static_path = ['_static']

html_logo = 'images/logo.png'

html_copy_source = False
html_show_sphinx = False
html_baseurl = 'https://path/to/docs'
html_use_opensearch = 'https://path/to/docs'
html_split_index = True
html_last_updated_fmt = '%m/%d/%Y'

# Configuration to add the "Edit on Github" badge on all pages
html_context = {
    'display_github': True,
    'github_user': 'McManning',
    'github_repo': 'Coherence',
    'github_version': 'master',
    'conf_py_path': '/Documentation~/',
}

html_theme_options = {
    'navigation_depth': 4,
    'display_version': True,
}

# Mock everything from Blender's API
autodoc_mock_imports = ['bpy', 'bgl', 'blf', 'mathutils', 'gpu', 'gpu_extras', 'numpy']

# Reference:
# https://github.com/sobotka/blender/blob/master/doc/python_api/sphinx_doc_gen.py

# Napoleon configurations
# Via: https://sphinxcontrib-napoleon.readthedocs.io/en/latest/sphinxcontrib.napoleon.html#sphinxcontrib.napoleon.Config
napoleon_include_init_with_doc = True
napoleon_include_special_with_doc = True

# Hide module names - redundant with how
# we document sections
add_module_names = False

# Intersphinx mapping to Blender's documentation

intersphinx_mapping = {
    # Ref: python -msphinx.ext.intersphinx https://docs.blender.org/api/current/objects.inv
    'bpy': ('https://docs.blender.org/api/current', ('./intersphinx-bpy.current.inv', None)),
}

