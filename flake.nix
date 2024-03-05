{
  description = "Hyprland RPC";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";
    nuget-packageslock2nix = {
      url = "github:mdarocha/nuget-packageslock2nix/main";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, nuget-packageslock2nix, ... }: let
    systems = ["arrch64-linux" "i686-linux" "x86_64-linux"];
    forAllSystems = nixpkgs.lib.genAttrs systems;
    pkgs = nixpkgs.legacyPackages;
  in {
    packages = forAllSystems (system: {
      default = pkgs.${system}.buildDotnetModule {
          pname = "hyprrpc";
          version = "1.0.0";

          src = self; 

          dotnet-sdk = pkgs.${system}.dotnetCorePackages.sdk_8_0;
          dotnet-runtime = pkgs.${system}.dotnetCorePackages.runtime_8_0;

          projectFile = "hyprrpc.csproj";
          nugetDeps = nuget-packageslock2nix.lib {
            inherit system;
            name = "hyprrpc";
            lockfiles = [
              ./packages.lock.json
            ];
          };
        };
    });
  };
}
