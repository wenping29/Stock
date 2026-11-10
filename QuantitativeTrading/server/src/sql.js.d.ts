declare module 'sql.js' {
  interface SqlJsStatic {
    Database: new (data?: ArrayLike<number> | Buffer | null) => Database;
  }
  interface Database {
    run(sql: string, params?: any[]): void;
    exec(sql: string): QueryResult[];
    export(): Uint8Array;
    close(): void;
  }
  interface QueryResult {
    columns: string[];
    values: any[][];
  }
  export default function initSqlJs(config?: any): Promise<SqlJsStatic>;
  export { Database, SqlJsStatic };
}
