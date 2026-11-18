import contextlib
import io
from pathlib import Path
import tempfile
import unittest

from check import main


class ProjectReferenceCheckTests(unittest.TestCase):
    def setUp(self):
        temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(temporary_directory.cleanup)
        self.production_directory = Path(temporary_directory.name) / "Server" / "src"

    def project(self, name, body=""):
        directory = self.production_directory / name
        directory.mkdir(parents=True)
        project = directory / f"{name}.csproj"
        project.write_text(f"<Project>{body}</Project>", encoding="utf-8")
        return project

    def run_check(self):
        diagnostics = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(diagnostics):
            exit_code = main(self.production_directory)
        return exit_code, diagnostics.getvalue()

    def test_rejects_forbidden_reference_even_without_compiled_code(self):
        self.project("Dfo.Server.Infrastructure")
        self.project(
            "Dfo.Server.Application",
            '<ItemGroup><ProjectReference Include="../Dfo.Server.Infrastructure/'
            'Dfo.Server.Infrastructure.csproj" /></ItemGroup>',
        )

        exit_code, diagnostics = self.run_check()

        self.assertEqual(1, exit_code)
        self.assertIn("Dfo.Server.Application.csproj", diagnostics)
        self.assertIn("Dfo.Server.Infrastructure.csproj", diagnostics)

    def test_rejects_new_project_without_a_policy(self):
        self.project("Dfo.Server.NewFeature")

        exit_code, diagnostics = self.run_check()

        self.assertEqual(1, exit_code)
        self.assertIn("Dfo.Server.NewFeature.csproj", diagnostics)
        self.assertIn("no dependency policy", diagnostics)

    def test_rejects_reference_from_an_import_active_in_ci(self):
        self.project("Dfo.Server.Infrastructure")
        application = self.project(
            "Dfo.Server.Application",
            '<Import Project="ExtraReferences.props" />',
        )
        application.with_name("ExtraReferences.props").write_text(
            '<Project><ItemGroup Condition="\'$(Configuration)\' == \'Release\' '
            'And \'$(ContinuousIntegrationBuild)\' == \'true\'">'
            '<ProjectReference Include="../Dfo.Server.Infrastructure/Dfo.Server.Infrastructure.csproj" />'
            '</ItemGroup></Project>',
            encoding="utf-8",
        )

        exit_code, diagnostics = self.run_check()

        self.assertEqual(1, exit_code)
        self.assertIn("Dfo.Server.Infrastructure.csproj", diagnostics)
        self.assertIn("forbidden direct reference", diagnostics)

    def test_accepts_host_reaching_protocol_through_the_game_server(self):
        self.project("Dfo.Server.Protocol")
        self.project(
            "Dfo.Server.GameServer",
            '<ItemGroup><ProjectReference Include="../Dfo.Server.Protocol/../Dfo.Server.Protocol/'
            'Dfo.Server.Protocol.csproj" /></ItemGroup>',
        )
        self.project(
            "Dfo.Server.Host",
            '<ItemGroup><ProjectReference Include="../Dfo.Server.GameServer/'
            'Dfo.Server.GameServer.csproj" /></ItemGroup>',
        )

        exit_code, diagnostics = self.run_check()

        self.assertEqual(0, exit_code, diagnostics)
