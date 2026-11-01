// Applies the names of the program index (docs/rules/program-index.md, section "By address") to the
// program: a function name at a code address, creating the function when there is none, and a label at
// a data address. Run headless (see tools/GhidraDump/README.md); the first script argument is the path
// of the index.
//@category OpenArmada

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.SourceType;

public class ApplyIndexNames extends GhidraScript {

    private static final Pattern ROW = Pattern.compile("^\\| (0x[0-9a-f]+) \\| (\\w+) \\|");

    @Override
    protected void run() throws Exception {
        String text = Files.readString(Path.of(getScriptArgs()[0]));
        int start = text.indexOf("\n## By address");
        if (start < 0) {
            throw new IllegalArgumentException("no section \"By address\" in the index");
        }
        int end = text.indexOf("\n## ", start + 1);
        int functions = 0;
        int labels = 0;
        int failed = 0;
        for (String line : text.substring(start, end < 0 ? text.length() : end).split("\n")) {
            Matcher m = ROW.matcher(line);
            if (!m.find()) {
                continue;
            }
            Address address = toAddr(m.group(1));
            String name = m.group(2);
            MemoryBlock block = currentProgram.getMemory().getBlock(address);
            try {
                if (block != null && block.isExecute()) {
                    Function function = getFunctionAt(address);
                    if (function == null) {
                        if (getInstructionAt(address) == null) {
                            disassemble(address);
                        }
                        function = createFunction(address, name);
                    }
                    if (function == null) {
                        printerr("ApplyIndexNames: no function at " + address + " for " + name);
                        failed++;
                        continue;
                    }
                    function.setName(name, SourceType.USER_DEFINED);
                    functions++;
                } else {
                    createLabel(address, name, true, SourceType.USER_DEFINED);
                    labels++;
                }
            } catch (Exception e) {
                printerr("ApplyIndexNames: " + address + " " + name + ": " + e.getMessage());
                failed++;
            }
        }
        println("ApplyIndexNames: functions " + functions + ", labels " + labels + ", failed " + failed);
    }
}
