# Web

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.2.

## Product design

Follow the [SVM design system](../.kiro/steering/design-system.md) for the approved light theme, app frame, responsive navigation, typography, and accessibility. Follow the [dashboard surface contract](../docs/ui/dashboard.md) for dashboard composition and behavior. Runtime design tokens are defined in `src/styles.css`. Preserve the existing Dashboard, Vulnerabilities, Intake, Remediation, Verification, Admin, and Audit routes when updating the shell. Earlier Ferrari and dark SOC guidance is historical.

For login UX and the distinction between real API authentication and the explicit browser demo, see the [authentication UI contract](../docs/ui/authentication.md). The default profile uses server sessions and permission policies. Use `npm run start:demo` for sample data; browser demo state does not authorize real API requests.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
