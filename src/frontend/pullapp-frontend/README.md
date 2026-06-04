- `apps/native` – a [react-native](https://reactnative.dev/) app built with [expo](https://docs.expo.dev/)
- `apps/web` – a [Next.js](https://nextjs.org/) app built with [react-native-web](https://necolas.github.io/react-native-web/)


- `packages/domain` – model classes, interfaces
- `packages/app-client` – concrete implementations that call APIs
- `packages/features` – business logic, use cases
- `packages/ui` – a [react-native](https://reactnative.dev/) component library shared by both `web` and `native`


- `packages/typescript-config` – `tsconfig.json`s used throughout the monorepo
- [TypeScript](https://www.typescriptlang.org/) – static type checking
- [Prettier](https://prettier.io) – code formatting

Run:
```bash
pnpm install
pnpm run dev # --filter @pullapp/web
```
