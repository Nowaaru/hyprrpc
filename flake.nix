{
  description = "A simple Hyprland-focused RPC manager written in C#.";

  inputs.nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";

  outputs = {
    self,
    nixpkgs,
  }: let
    systems = ["aarch64-linux" "i686-linux" "x86_64-linux"];
    forAllSystems = nixpkgs.lib.genAttrs systems;
  in {
    packages = forAllSystems (system: {
      default = with nixpkgs.legacyPackages.${system};
        stdenv.mkDerivation {
          name = "hyprrpc";
          version = "1.0.0";

          src = self;
          installPhase = ''
          '';
        };
      # packages.x86_64-linux.hello = nixpkgs.legacyPackages.x86_64-linux.hello;

      # packages.x86_64-linux.default = self.packages.x86_64-linux.hello;
    });
  };
}
