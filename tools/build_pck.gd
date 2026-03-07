extends SceneTree

func _initialize():
	var output_dir := "res://build"
	var output_file := "res://build/RemoveMultiplayerPlayerLimit.pck"
	var manifest_path := "res://mod_manifest.json"
	DirAccess.make_dir_recursive_absolute(output_dir)
	var packer := PCKPacker.new()
	var ok := packer.pck_start(output_file)
	if ok != OK:
		push_error("pck_start failed: %s" % ok)
		quit(1)
	var files := PackedStringArray([
		manifest_path
	])
	if FileAccess.file_exists("res://mod_image.png"):
		files.append("res://mod_image.png")
	for file in files:
		var add_ok := packer.add_file(file, file)
		if add_ok != OK:
			push_error("add_file failed: %s %s" % [file, add_ok])
			quit(1)
	var flush_ok := packer.flush()
	if flush_ok != OK:
		push_error("flush failed: %s" % flush_ok)
		quit(1)
	print("PCK built: %s" % output_file)
	quit(0)
