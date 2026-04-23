from setuptools import setup
from pybind11.setup_helpers import Pybind11Extension

ext = Pybind11Extension("router", ["bindings.cpp"])

setup(ext_modules=[ext])